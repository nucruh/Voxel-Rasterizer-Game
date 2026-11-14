using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Concurrent;
using System.Diagnostics;
using static BitmapFontRenderer;
using static MeshUtility;
using static EngineSettings;
using static FileRead;
using GameWindow = OpenTK.Windowing.Desktop.GameWindow;

using var game = new Game();
game.Run();

public class Game : GameWindow
{
    #region Rendering
    // Shaders
    private Shader _shader = null!;
    private Shader _shadowShader = null!;
    private Shader _foliageShader = null!;
    private Shader _skyShader = null!;


    // Atlases
    public static TextureAtlas _atlas = null!;
    public static TextureAtlas _normalAtlas = null!;
    public static TextureAtlas _tintAtlas = null!;
    public static TextureAtlas _whittakerAtlas = null!;

    // Shadows
    private const int ShadowMapSize = 4096;
    private ShadowMap _shadow = null!;

    // Light
    private Matrix4 _lightSpace;
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f)); // direction the light TRAVELS
    private float _texelWorld;

    #endregion Rendering

    #region Bindings
    private int _skyVao;
    #endregion Bindings

    #region Camera and Scene
    public static Camera _camera = null!;
    public static KeyboardState _keyboardState = null!;

    public static List<Mesh> _sceneObjects = new List<Mesh>();
    public static ConcurrentDictionary<Vector3i, Chunk> _chunks = new();
    public static Dictionary<Vector3i, bool> _chunkGenerateQueue = new();

    private Frustum cameraFrustum = new Frustum();
    private Frustum lightFrustum = new Frustum();

    public static int _worldSeed = 1234;
    public static readonly int WorldChunks = 6;
    private bool _wireframe = false;
    #endregion Camera and Scene

    #region Statistics
    public static double totalGenerationTime = 0.0f;
    private readonly VramMonitor _vram = new();

    // Timers
    private double _time = 0.0;
    private float shadowTimer = 0.0f;
    private float infoTimer = 0.0f;
    private float chunkUpdateTimer = 0.0f;

    // Measurements
    private float ramUsage = 0.0f;
    private float vramUsage = 0.0f;
    #endregion Statistics

    #region UserInterface
    private Frame _menu = null!;
    private Frame _perfMenu = null!;

    private bool perfMenuVisible = true;
    private bool doMaxResolution = false;
    private bool showMenu = false;
    private bool _ignoreMouse = false;
    #endregion UserInterface


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



    protected override void OnLoad()
    {
        base.OnLoad();

        SystemInfo.gpuVendor = GL.GetString(StringName.Vendor);
        SystemInfo.gpuRenderer = GL.GetString(StringName.Renderer);
        SystemInfo.glslVersion = GL.GetString(StringName.ShadingLanguageVersion);

        BlockRegistry.Load("Models/blocks.json");

        BlockDefinition dirt = BlockRegistry.Blocks[1];

        Console.WriteLine(dirt.Name);
        Console.WriteLine(dirt.DisplayName);
        Console.WriteLine(dirt.ItemSize);
        Console.WriteLine(dirt.Textures[2]);
        Console.WriteLine(dirt.Tags.Contains("grass_spreadable"));

        InitTextRendering(ClientSize.X, ClientSize.Y);
        ImageRenderer.Init(ClientSize.X, ClientSize.Y);
        ramUsage = PerfMonitors.GetRamUsage();
        vramUsage = _vram.GetDedicatedBytes(0);

        _camera = new Camera(new Vector3(3, 40, 4), Vector3.Zero);
        CursorState = CursorState.Grabbed;

        GL.Enable(EnableCap.DepthTest);

        _shader = new Shader("Shaders/main.vert", "Shaders/main.frag");
        _foliageShader = new Shader("Shaders/foliage.vert", "Shaders/foliage.frag");
        _shadowShader = new Shader("Shaders/shadow.vert", "Shaders/shadow.frag");
        _skyShader = new Shader("Shaders/sky.vert", "Shaders/sky.frag");

        _atlas = new TextureAtlas(TextureNames.DiffuseFolder, TextureNames.Names);
        // this needs to be changed to autogenerrate normals
        _normalAtlas = new TextureAtlas(TextureNames.NormalFolder, TextureNames.Names, TextureNames.NormalNames, 256);
        _tintAtlas = new TextureAtlas(TextureNames.TintFolder, TextureNames.Names, TextureNames.TintNames, 32);
        _whittakerAtlas = new TextureAtlas("Images/WhittakerAtlas.png");

        Models.Initialize();
        WorldPregeneration.Pregenerate();

        _shadow = new ShadowMap(ShadowMapSize);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _skyVao = GL.GenVertexArray();

        float worldW = WorldChunks * Chunk.Size;
        _lightSpace = BuildLightSpaceFit(
            _lightDir,
            new Vector3(0, 0, 0),
            new Vector3(worldW, WORLD_HEIGHT, worldW),
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
                    Position = new UDim2(0.5f, 10, 0.0f, 0),
                    AnchorPoint = (0.5f, 0.0f),
                    Children = {
                        new Label("Block name: null", "BlockName"),
                    },
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
                        new Label($"Camera Position: {_camera.Position}", "CameraPosition"),
                    }
                }
                
            },

        };
    }

    static Matrix4 BuildLightSpaceFit(
        Vector3 lightDir,
        Vector3 cameraPosition,
        Vector3 boundsSize,
        int mapSize,
        out float texelWorld)
    {
        Vector3 center = cameraPosition;

        Vector3 up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        Matrix4 view = Matrix4.LookAt(center - lightDir, center, up);

        Vector3 halfSize = boundsSize * 0.5f;

        Vector3 boundsMin = center - halfSize;
        Vector3 boundsMax = center + halfSize;

        Vector3 lo = new(float.MaxValue);
        Vector3 hi = new(float.MinValue);

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new(
                (i & 1) == 0 ? boundsMin.X : boundsMax.X,
                (i & 2) == 0 ? boundsMin.Y : boundsMax.Y,
                (i & 4) == 0 ? boundsMin.Z : boundsMax.Z
            );

            Vector3 v = Vector3.TransformPosition(corner, view);

            lo = Vector3.ComponentMin(lo, v);
            hi = Vector3.ComponentMax(hi, v);
        }

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
            if(mesh.Active != true)
                continue;

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

        if (!IsFocused)
            return;

        float dt = (float)e.Time;

        _keyboardState = KeyboardState;

        /*
        float speed = _camera.Speed * (KeyboardState.IsKeyDown(Keys.LeftControl) ? 3f : 1f);
        speed = speed * (KeyboardState.IsKeyDown(Keys.LeftShift) ? 0.1f : 1f);

        Vector3 flatFront = Vector3.Normalize(new Vector3(_camera.Front.X, 0, _camera.Front.Z));
        Vector3 move = Vector3.Zero;

        if (KeyboardState.IsKeyDown(Keys.W)) move += _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.D)) move += _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.A)) move -= _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.Space)) move += Vector3.UnitY;
        */

        if (KeyboardState.IsKeyPressed(Keys.F2))
            _wireframe = !_wireframe;
            GL.PolygonMode(TriangleFace.FrontAndBack, _wireframe ? PolygonMode.Line : PolygonMode.Fill);

        if(KeyboardState.IsKeyPressed(Keys.F1))
            perfMenuVisible = !perfMenuVisible;

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

        /*if (move != Vector3.Zero)
            _camera.Position += Vector3.Normalize(move) * speed * dt; */

        if (KeyboardState.IsKeyPressed(Keys.Escape))
        {
            showMenu = !showMenu;
            if (showMenu)
                CursorState = CursorState.Normal;
            else
                CursorState = CursorState.Grabbed;

        }

        if (_ignoreMouse)
        {
            _ignoreMouse = false;
            return;
        }

        if(!showMenu)
            _camera.Look(MouseState.Delta);

        chunkUpdateTimer += (float)e.Time;

        if (chunkUpdateTimer >= CHUNK_UPDATE_RATE)
        {
            chunkUpdateTimer = 0;
            DynamicChunks.HandleChunks();
        }
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
            _lightSpace = BuildLightSpaceFit(
                _lightDir,
                _camera.Position,
                new Vector3(128, WORLD_HEIGHT, 128),
                ShadowMapSize,
                out _texelWorld);

            shadowTimer = 0;
            RenderShadowMap();
        }

        infoTimer += (float)e.Time;
        if(infoTimer >= 1.0 / INFO_TIMER_RATE)
        {
            infoTimer = 0;
            ramUsage = PerfMonitors.GetRamUsage();
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
            0.1f, // near plane
            1000f); // far plane

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

        _lightDir = Vector3.Transform(
            _lightDir,
            Quaternion.FromAxisAngle(Vector3.UnitX, 1f * (float)e.Time)
        );

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

        GL.ActiveTexture(TextureUnit.Texture3);
        _tintAtlas.Bind(TextureUnit.Texture3);
        _shader.SetInt("tintAtlas", 3);

        GL.ActiveTexture(TextureUnit.Texture4);
        _whittakerAtlas.Bind(TextureUnit.Texture4);
        _shader.SetInt("whittakerAtlas", 4);

        cameraFrustum.Update(view * projection);
        foreach (var mesh in _sceneObjects)
        {
            if (mesh.Active != true)
                continue;

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

        GL.ActiveTexture(TextureUnit.Texture3);
        _tintAtlas.Bind(TextureUnit.Texture3);
        _foliageShader.SetInt("tintAtlas", 3);

        GL.ActiveTexture(TextureUnit.Texture4);
        _whittakerAtlas.Bind(TextureUnit.Texture4);
        _foliageShader.SetInt("whittakerAtlas", 4);

        GL.Disable(EnableCap.CullFace);

        foreach (var mesh in _sceneObjects)
        {
            if (!mesh.Active)
                continue;

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

            var camPos = _perfMenu.Find("CameraPosition") as Label;
            camPos?.Text = $"Camera Position: {_camera.Position}";

            RaycastHit hit;
            VoxelRaycaster.Raycast(_camera.Position, _camera.Front, 15f, false, out hit);

            var blockName = _perfMenu.Find("BlockName") as Label;
            blockName?.Text = $"Block name: {(BlockRegistry.Blocks.TryGetValue(hit.BlockId, out var block) ? block?.DisplayName : "null") ?? "null"}";

            Ui.RenderScreen(_perfMenu);
        }

        if(showMenu)
        {
            Ui.RenderScreen(_menu);
        }

        GameWorld.Heartbeat((float)e.Time);

        UiRenderQueue.Flush();

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        foreach (var mesh in _sceneObjects)
            mesh.Dispose();
        _atlas.Dispose();
        _normalAtlas.Dispose();
        _tintAtlas.Dispose();
        _whittakerAtlas.Dispose();
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