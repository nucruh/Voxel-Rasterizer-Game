using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using static BitmapFontRenderer;
using static MeshUtility;
using GameWindow = OpenTK.Windowing.Desktop.GameWindow;

using var game = new Game();
game.Run();

public class Game : GameWindow
{
    private Shader _shader = null!;
    private List<Mesh> _sceneObjects = new List<Mesh>();
    private Camera _camera = null!;
    public static TextureAtlas _atlas = null!;
    public static TextureAtlas _normalAtlas = null!;
    private bool _wireframe = false;

    public static int _worldSeed = 1234;

    // --- shadow mapping ---
    private const int ShadowMapSize = 4096;
    private const int WorldChunks = 6;
    //private Chunk[,,] _chunks = new Chunk[WorldChunks, 3, WorldChunks];
    private ConcurrentDictionary<Vector3i, Chunk> _chunks = new();
    private const float WorldHeight = 96f;   // replace with your max terrain height

    public double totalGenerationTime = 0.0f;

    private Shader _shadowShader = null!;
    private ShadowMap _shadow = null!;
    private Shader _foliageShader = null!;
    private Matrix4 _lightSpace;
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f)); // direction the light TRAVELS
    private float _texelWorld;
    private double _time;

    private Shader _skyShader = null!;
    private int _skyVao;
    private readonly VramMonitor _vram = new();

    private Frustum cameraFrustum = new Frustum();
    private Frustum lightFrustum = new Frustum();

    private float shadowTimer = 0.0f;
    private const float SHADOW_MAP_RATE = 20.0f;

    private float infoTimer = 0.0f;
    private const float INFO_TIMER_RATE = 0.5f;

    private float ramUsage = 0.0f;
    private float vramUsage = 0.0f;

    private Frame _menu = null!;

    private Frame _perfMenu = null!;

    private bool perfMenuVisible = true;

    private bool doMaxResolution = false;
    private bool showMenu = false;

    //prevents snapping when resizing window
    private bool _ignoreMouse = false;


    public Game() : base(
        GameWindowSettings.Default,
        new NativeWindowSettings
        {
            ClientSize = Monitors.GetPrimaryMonitor()!.ClientArea.Size,
            Location = Vector2i.Zero,
            Title = "Rasterizer",
            APIVersion = new Version(4, 3),
            Profile = ContextProfile.Core,
            Vsync = VSyncMode.On,
            WindowBorder = WindowBorder.Hidden
        })
    {
    }

    public sealed class VramMonitor
    {
        private readonly List<PerformanceCounter> _dedicated = [];
        private readonly string _pidPrefix = $"pid_{Environment.ProcessId}_";
        private double _nextPoll;
        private long _cachedBytes;

        // Call once per frame; pass total elapsed seconds. Polls twice per second.
        public long GetDedicatedBytes(double timeSeconds)
        {
            if (!OperatingSystem.IsWindows()) return -1;
            if (timeSeconds < _nextPoll) return _cachedBytes;
            _nextPoll = timeSeconds + 0.5;

            try
            {
                // Instances only appear once the GPU is in use, so enumerate lazily.
                if (_dedicated.Count == 0)
                {
                    var cat = new PerformanceCounterCategory("GPU Process Memory");
                    foreach (string inst in cat.GetInstanceNames())
                        if (inst.StartsWith(_pidPrefix))
                            _dedicated.Add(new PerformanceCounter(
                                "GPU Process Memory", "Dedicated Usage", inst, true));
                }

                long sum = 0;
                foreach (var c in _dedicated)
                    sum += (long)c.NextValue();

                _cachedBytes = sum;
            }
            catch
            {
                _dedicated.Clear(); // instance vanished, re-enumerate next poll
            }

            return _cachedBytes;
        }
    }

    long GetRamUsage()
    {
        using Process process = Process.GetCurrentProcess();
        return process.WorkingSet64 / (1024 * 1024);
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        SystemInfo.gpuVendor = GL.GetString(StringName.Vendor);
        SystemInfo.gpuRenderer = GL.GetString(StringName.Renderer);
        SystemInfo.glslVersion = GL.GetString(StringName.ShadingLanguageVersion);

        InitTextRendering(ClientSize.X, ClientSize.Y);
        ImageRenderer.Init(ClientSize.X, ClientSize.Y);
        ramUsage = GetRamUsage();
        vramUsage = _vram.GetDedicatedBytes(0);

        _camera = new Camera(new Vector3(3, 2, 4), Vector3.Zero);
        CursorState = CursorState.Grabbed;

        GL.Enable(EnableCap.DepthTest);

        _shader = new Shader(
            File.ReadAllText("Shaders/main.vert"),
            File.ReadAllText("Shaders/main.frag"));

        _foliageShader = new Shader(
            File.ReadAllText("Shaders/foliage.vert"),
            File.ReadAllText("Shaders/foliage.frag"));

        _atlas = new TextureAtlas(TextureNames.DiffuseFolder, TextureNames.Names);
        // The normal atlas uses the dedicated 256x256 normal texture for every slot.
        // this needs to be changed to autogenerrate normals
        _normalAtlas = new TextureAtlas(
            TextureNames.NormalFolder,
            TextureNames.Names,
            TextureNames.NormalNames,
            256);

        Models.Initialize();

        Stopwatch sw = Stopwatch.StartNew();

        // firs paralell pass; gen chunks
        Parallel.For(0, WorldChunks, x =>
        {
            for (int z = 0; z < WorldChunks; z++)
                for (int y = 0; y < 3; y++)
                {
                    Chunk c = new Chunk(new Vector3i(x * Chunk.Size, y * Chunk.Size, z * Chunk.Size));
                    c.Generate();
                    _chunks[new Vector3i(x, y, z)] = c;
                }
        });

        // second parallel pass; overflow
        Parallel.ForEach(_chunks.Values, c =>
        {
            // overflow blocks generated in first pass
            c.ApplyOverflow(_chunks);
        });

        // third parallel pass; mesh gen
        Stopwatch swmesh = Stopwatch.StartNew();
        Parallel.ForEach(_chunks.Values, c =>
        {
            c.CreateMesh(_chunks);
        });


        // fourth pass; upload meshes to gpu, add to scene
        foreach (var c in _chunks.Values)
        {
            c.ApplyMesh();
            _sceneObjects.Add(c.mesh!);

            if (c.foliageMesh != null)
                _sceneObjects.Add(c.foliageMesh);
        }
        swmesh.Stop();

        totalGenerationTime = sw.Elapsed.TotalMilliseconds;
        sw.Stop();

        Console.WriteLine(swmesh.Elapsed.TotalMilliseconds);

        // --- shadow setup (world is static, so the map is rendered once) ---
        _shadowShader = new Shader(
            File.ReadAllText("Shaders/shadow.vert"),
            File.ReadAllText("Shaders/shadow.frag"));

        _shadow = new ShadowMap(ShadowMapSize);

        _skyShader = new Shader(
            File.ReadAllText("Shaders/sky.vert"),
            File.ReadAllText("Shaders/sky.frag"));

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _skyVao = GL.GenVertexArray();

        // maybe lowering this can help in needing less resolution for the shadowmap
        float worldW = WorldChunks * Chunk.Size;
        _lightSpace = BuildLightSpaceFit(
            _lightDir,
            new Vector3(0, 0, 0),
            new Vector3(worldW, WorldHeight, worldW),
            ShadowMapSize,
            out _texelWorld);


        int icon = ImageRenderer.Load("Images/Thumbnail.jpg");   // load once, not every frame
        _menu = new Frame
        {
            Children =
            {
                new Image(icon)
                {
                    Size = new UDim2(0, 300, 0, 150),
                    Position = new UDim2(0.5f, 0, 0.35f, 0),
                    AnchorPoint = (0.5f, 0.5f)
                },

                new Stack
                {
                    Gap = 6,
                    Position = new UDim2(0.5f, 0, 0.5f, 0),
                    AnchorPoint = (0.5f, 0.5f),
                    Children =
                    {
                        new Label("MAIN MENU"),
                        new Button("QUIT",  () => Close())
                    }
                }
            }
        };

        _perfMenu = new Frame
        {
            Children =
            {
                new Rect
                {
                    Size = new UDim2(0.17f, 0.0f, 0.27f, 0.0f),
                    Position = new UDim2(0.0f, 5, 0.0f, 5),
                    Alpha = 0.6f,
                    Color = new TextColor(15, 20, 28)
                },

                new Stack
                {
                    Gap = 5,
                    Position = new UDim2(0.0f, 10, 0.0f, 10),
                    AnchorPoint = (0.0f, 0.0f),
                    Children =
                    {
                        new Label("F1 to open / close", "F1Label"),
                        new Label("F2 to toggle wireframe", "F2Label"),
                        new Rect
                        {
                            W = 0,
                            H = 20,
                            Position = new UDim2(0.0f, 10, 0.0f, 10),
                            Alpha = 1.0f,
                            Color = new TextColor(15, 20, 28),
                            Name = "PerfRect"
                        },
                        new Label("-   PERFORMANCE   -", "PerfTitle"),
                        new Label("RAM : 0 MB", "UsageRAM"),
                        new Label("VRAM : 0 MB", "UsageVRAM"),
                        new Label("FPS : 0", "FPS"),
                        new Label("Triangles : 0", "Triangles"),
                        new Label("Gen time : 0 ms", "GenTime"),
                        new Label("Draw calls : 0", "DrawCalls"),

                        new Rect
                        {
                            W = 0,
                            H = 20,
                            Position = new UDim2(0.0f, 10, 0.0f, 10),
                            Alpha = 1.0f,
                            Color = new TextColor(15, 20, 28)
                        },
                        new Label("-    GAME DATA    -"),
                        new Label($"Screen : {ClientSize.X} x {ClientSize.Y}", "Screen"),
                        new Label($"GPU : {SystemInfo.gpuRenderer}"),
                        new Label($"Camera Position: {_camera.Position}"),
                    }
                }
                
            },

        };

        RenderShadowMap();
    }

    static Matrix4 BuildLightSpaceFit(Vector3 lightDir, Vector3 boundsMin, Vector3 boundsMax, int mapSize, out float texelWorld)
    {
        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4 view = Matrix4.LookAt(center - lightDir, center, up);

        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new(
                (i & 1) == 0 ? boundsMin.X : boundsMax.X,
                (i & 2) == 0 ? boundsMin.Y : boundsMax.Y,
                (i & 4) == 0 ? boundsMin.Z : boundsMax.Z);

            Vector3 v = Vector3.TransformPosition(corner, view);
            lo = Vector3.ComponentMin(lo, v);
            hi = Vector3.ComponentMax(hi, v);
        }

        // view space looks down -Z, so near/far come from the negated z extents
        float pad = 2f;

        texelWorld = MathF.Max(hi.X - lo.X, hi.Y - lo.Y) / mapSize;

        Matrix4 proj = Matrix4.CreateOrthographicOffCenter(lo.X, hi.X, lo.Y, hi.Y, -hi.Z - pad, -lo.Z + pad);
        return view * proj;
    }

    void RenderShadowMap()
    {
        GL.Viewport(0, 0, _shadow.Size, _shadow.Size);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadow.Fbo);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        _shadowShader.Use();
        _shadowShader.SetMatrix4("model", Matrix4.Identity);
        _shadowShader.SetMatrix4("lightSpace", _lightSpace);
        _shadowShader.SetFloat("time", (float)_time);

        _atlas.Bind(TextureUnit.Texture0);
        _shadowShader.SetInt("atlas", 0);

        _normalAtlas.Bind(TextureUnit.Texture1);
        _shadowShader.SetInt("normalAtlas", 1);

        lightFrustum.Update(_lightSpace);

        foreach (var mesh in _sceneObjects)
        {
            _shadowShader.SetInt("isFoliage", mesh.IsFoliage ? 1 : 0);

            Vector3 min = mesh._meshPosition - new Vector3(0.8f);
            Vector3 max = mesh._meshPosition + new Vector3(Chunk.Size - 0.2f);

            if (!lightFrustum.IsBoxVisible(min, max))
                continue;

            if (mesh.IsFoliage)
            {
                //continue;
                GL.Disable(EnableCap.CullFace);
            }
                

            mesh.Draw();

            if (mesh.IsFoliage)
                GL.Enable(EnableCap.CullFace);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        ResizeText(e.Width, e.Height);

        CursorState = CursorState.Grabbed;

        _ignoreMouse = true;
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);

        //if (KeyboardState.IsKeyDown(Keys.Escape))
            //Close();

        if (!IsFocused)
            return;



        float dt = (float)e.Time;
        float speed = _camera.Speed * (KeyboardState.IsKeyDown(Keys.LeftControl) ? 3f : 1f);
        speed = speed * (KeyboardState.IsKeyDown(Keys.LeftShift) ? 0.1f : 1f);

        Vector3 flatFront = Vector3.Normalize(new Vector3(_camera.Front.X, 0, _camera.Front.Z));
        Vector3 move = Vector3.Zero;

        if (KeyboardState.IsKeyDown(Keys.W)) move += _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.D)) move += _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.A)) move -= _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.Space)) move += Vector3.UnitY;

        if (KeyboardState.IsKeyPressed(Keys.F2))
        {
            _wireframe = !_wireframe;
            GL.PolygonMode(TriangleFace.FrontAndBack, _wireframe ? PolygonMode.Line : PolygonMode.Fill);
        }

        if(KeyboardState.IsKeyPressed(Keys.F1))
        {
            perfMenuVisible = !perfMenuVisible;
        }

        if(KeyboardState.IsKeyPressed(Keys.F11))
        {
            doMaxResolution = !doMaxResolution;
            if (doMaxResolution)
            {
                WindowBorder = WindowBorder.Hidden;
                var primaryMonitor = Monitors.GetPrimaryMonitor();
                ClientSize = primaryMonitor!.ClientArea.Size;
                Location = Vector2i.Zero;

            }
            else
            {
                ClientSize = new Vector2i(1280, 720);
                Location = SystemInfo.screenCenter - new Vector2i(1280 / 2, 720 / 2);
                WindowBorder = WindowBorder.Resizable;
            }
        }

        if (move != Vector3.Zero)
            _camera.Position += Vector3.Normalize(move) * speed * dt;

        if (KeyboardState.IsKeyPressed(Keys.Escape))
        {
            showMenu = !showMenu;

            if (showMenu)
            {
                CursorState = CursorState.Normal;
            }
            else
            {
                CursorState = CursorState.Grabbed;
            }

        }

        if (_ignoreMouse)
        {
            _ignoreMouse = false;
            return;
        }

        if(!showMenu)
            _camera.Look(MouseState.Delta);
    }

    static private int countFrame = 0;
    static private double timeFrame = 0;
    static private double lastFps = 0;
    static private double timeSinceLastFps = 0;

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        countFrame++;
        timeFrame += 1.0 / e.Time;
        timeSinceLastFps += e.Time;

        _time += e.Time;

        uint drawCalls = 0;

        shadowTimer += (float)e.Time;
        if(shadowTimer >= 1.0 / SHADOW_MAP_RATE)
        {
            shadowTimer = 0;
            RenderShadowMap();
        }

        infoTimer += (float)e.Time;
        if(infoTimer >= 1.0 / INFO_TIMER_RATE)
        {
            infoTimer = 0;
            ramUsage = GetRamUsage();
            vramUsage = _vram.GetDedicatedBytes(_time);
        }

        if (timeSinceLastFps >= 0.5)
        {
            timeFrame /= countFrame;
            lastFps = timeFrame;
            countFrame = 0;
            timeSinceLastFps = 0;
        }

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        Matrix4 model = Matrix4.CreateRotationY(0f);
        Matrix4 view = _camera.GetViewMatrix();
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(70f),
            Size.X / (float)Math.Max(Size.Y, 1),
            0.1f,
            1000f);

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);

        // Render procedural sky
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);

        _skyShader.Use();

        _skyShader.SetMatrix4("invProjection", Matrix4.Invert(projection));
        _skyShader.SetMatrix4("invView", Matrix4.Invert(view));

        GL.BindVertexArray(_skyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);

        // Render regular meshes
        _shader.Use();

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        _shader.SetMatrix4("model", model);
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        _shader.SetMatrix4("lightSpace", _lightSpace);
        _shader.SetVector3("lightDir", _lightDir);
        _shader.SetFloat("texelWorld", _texelWorld);
        _shader.SetVector3("uCamPos", _camera.Position);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _shadow.Texture);
        _shader.SetInt("shadowMap", 1);

        GL.ActiveTexture(TextureUnit.Texture0);
        _atlas.Bind(TextureUnit.Texture0);
        _shader.SetInt("atlas", 0);

        GL.ActiveTexture(TextureUnit.Texture2);
        _normalAtlas.Bind(TextureUnit.Texture2);
        _shader.SetInt("normalAtlas", 2);

        cameraFrustum.Update(view * projection);
        foreach (var mesh in _sceneObjects)
        {
            Vector3 min = mesh._meshPosition - new Vector3(0.8f);
            Vector3 max = mesh._meshPosition + new Vector3(Chunk.Size - 0.2f);

            if(!cameraFrustum.IsBoxVisible(min, max))
                continue;

            if (!mesh.IsFoliage)
            {
                drawCalls++;
                mesh.Draw();
            }
               
        }

        // Render foliage
        _foliageShader.Use();

        _foliageShader.SetMatrix4("model", model);
        _foliageShader.SetMatrix4("view", view);
        _foliageShader.SetMatrix4("projection", projection);

        _foliageShader.SetMatrix4("lightSpace", _lightSpace);
        _foliageShader.SetVector3("lightDir", _lightDir);
        _foliageShader.SetFloat("texelWorld", _texelWorld);
        _foliageShader.SetVector3("uCamPos", _camera.Position);
        _foliageShader.SetFloat("time", (float)_time);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _shadow.Texture);
        _foliageShader.SetInt("shadowMap", 1);

        GL.ActiveTexture(TextureUnit.Texture0);
        _atlas.Bind(TextureUnit.Texture0);
        _foliageShader.SetInt("atlas", 0);

        GL.ActiveTexture(TextureUnit.Texture2);
        _normalAtlas.Bind(TextureUnit.Texture2);
        _foliageShader.SetInt("normalAtlas", 2);

        GL.Disable(EnableCap.CullFace);

        foreach (var mesh in _sceneObjects)
        {
            Vector3 min = mesh._meshPosition - new Vector3(0.8f);
            Vector3 max = mesh._meshPosition + new Vector3(Chunk.Size - 0.2f);

            if (!cameraFrustum.IsBoxVisible(min, max))
                continue;

            if (mesh.IsFoliage)
            {
                drawCalls++;
                mesh.Draw();
            }
                
        }

        GL.Enable(EnableCap.CullFace);

        //RenderHud(drawCalls);

        var m = MouseState;
        Ui.SetMouse(m.X, m.Y, m.IsButtonDown(MouseButton.Left));





        if(perfMenuVisible)
        {
            var fps = _perfMenu.Find("FPS") as Label;
            fps?.Text = $"FPS       : {Math.Round(lastFps, 0)}";

            var ram = _perfMenu.Find("UsageRAM") as Label;
            ram?.Text = $"RAM       : {ramUsage} MB";

            var vram = _perfMenu.Find("UsageVRAM") as Label;
            vram?.Text = $"VRAM      : {vramUsage / (1024f * 1024f):f0} MB";

            var tris = _perfMenu.Find("Triangles") as Label;
            tris?.Text = $"Triangles : {GetTotalTriangles(_sceneObjects.ToArray()) / 1000:f1}k";

            var gen = _perfMenu.Find("GenTime") as Label;
            gen?.Text = $"Gen time  : {totalGenerationTime:f0} ms";

            var calls = _perfMenu.Find("DrawCalls") as Label;
            calls?.Text = $"Draw calls: {drawCalls}";

            var screen = _perfMenu.Find("Screen") as Label;
            screen?.Text = $"Screen    : {ClientSize.X} x {ClientSize.Y}";

            Ui.RenderScreen(_perfMenu);
        }

        if(showMenu)
        {
            Ui.RenderScreen(_menu);
        }

        UiRenderQueue.Flush();

        SwapBuffers();
    }

    private void RenderHud(uint drawCalls)
    {
        const float margin = 12f;
        const float panelWidth = 700f;
        const float panelHeight = 275f;

        RenderRect(margin, margin, panelWidth, panelHeight,
                   new TextColor(15, 20, 28), zIndex: 0, alpha: 0.88f);

        string data = $"RAM       : {ramUsage} MB\n" +
            $"VRAM      : {vramUsage / (1024f * 1024f):f0} MB\n" +
            $"FPS       : {Math.Round(lastFps, 0)}\n" +
            $"Triangles : {GetTotalTriangles(_sceneObjects.ToArray()) / 1000:f1}k\n" +
            $"Gen time  : {totalGenerationTime:f0} ms\n" +
            $"Draw calls: {drawCalls}\n" +
            $"Screen    : {ClientSize.X} x {ClientSize.Y}";

        RenderText(data,
            (margin + 10f) / ClientSize.X,
            (margin + 10f) / ClientSize.Y,
            white,
            zIndex: 1);

        RenderText("+", 0.5f, 0.5f,
                   new TextColor(255, 255, 255), zIndex: 2);
    }

    protected override void OnUnload()
    {
        foreach (var mesh in _sceneObjects)
            mesh.Dispose();
        _atlas.Dispose();
        _normalAtlas.Dispose();
        _shader.Dispose();
        _shadow.Dispose();
        _shadowShader.Dispose();
        GL.DeleteVertexArray(_skyVao);
        _skyShader.Dispose();
        _foliageShader.Dispose();
        DisposeText();
        base.OnUnload();
    }
}

class Shader : IDisposable
{
    private readonly int _program;

    public Shader(string vertexSource, string fragmentSource)
    {
        int vertex = Compile(ShaderType.VertexShader, vertexSource);
        int fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        _program = GL.CreateProgram();
        GL.AttachShader(_program, vertex);
        GL.AttachShader(_program, fragment);
        GL.LinkProgram(_program);

        GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out int success);

        if (success == 0)
            throw new Exception(GL.GetProgramInfoLog(_program));

        GL.DetachShader(_program, vertex);
        GL.DetachShader(_program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
    }

    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);

        if (success == 0)
            throw new Exception(GL.GetShaderInfoLog(shader));

        return shader;
    }

    public void Use() => GL.UseProgram(_program);

    public void SetMatrix4(string name, Matrix4 matrix)
    {
        GL.UniformMatrix4(GL.GetUniformLocation(_program, name), false, ref matrix);
    }

    public void SetVector3(string name, Vector3 v) =>
        GL.Uniform3(GL.GetUniformLocation(_program, name), v);

    public void SetFloat(string name, float v) =>
        GL.Uniform1(GL.GetUniformLocation(_program, name), v);

    public void SetInt(string name, int value) =>
        GL.Uniform1(GL.GetUniformLocation(_program, name), value);

    public void Dispose() => GL.DeleteProgram(_program);
}

class ShadowMap : IDisposable
{
    public readonly int Size;
    public readonly int Fbo;
    public readonly int Texture;

    public ShadowMap(int size)
    {
        Size = size;

        Texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32f,
            size, size, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, new float[] { 1f, 1f, 1f, 1f });

        // makes sampler2DShadow do the depth comparison in hardware
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);

        Fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, Texture, 0);
        GL.DrawBuffer(DrawBufferMode.None);   // depth only
        GL.ReadBuffer(ReadBufferMode.None);

        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new Exception("Shadow map framebuffer incomplete");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(Fbo);
        GL.DeleteTexture(Texture);
    }
}