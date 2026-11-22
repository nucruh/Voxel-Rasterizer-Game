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
    private Shader _postProcessShader = null!;


    // Atlases
    public static TextureAtlas _atlas = null!;
    public static TextureAtlas _normalAtlas = null!;
    public static TextureAtlas _tintAtlas = null!;
    public static TextureAtlas _whittakerAtlas = null!;

    // Shadows
    private const int CascadeCount = 3;
    private const int ShadowMapSize = 4096;
    private readonly float[] _cascadeSplits = { 48f, 128f, 512f};
    private ShadowMap[] _shadowCascades = null!;

    // Light
    private readonly Matrix4[] _lightSpaces = new Matrix4[CascadeCount];
    private readonly float[] _texelWorld = new float[CascadeCount];
    private Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f)); // direction the light TRAVELS

    #endregion Rendering

    #region Bindings
    private int _skyVao;
    private int _sceneFbo;
    private int _sceneColor;
    private int _sceneDepth;
    private int _sceneFoliageMask;
    #endregion Bindings

    #region Camera and Scene
    private const float NearPlane = 0.1f;
    private const float FarPlane = 30000f;

    public static Camera _camera = null!;
    public static KeyboardState _keyboardState = null!;

    public static List<Mesh> _sceneObjects = new List<Mesh>();
    public static ConcurrentDictionary<Vector3i, Chunk> _chunks = new();
    public static ConcurrentDictionary<Vector3i, byte> _chunkGenerateQueue = new();

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
    private float chunkUploadTimer = 0.0f;
    private float chunkGenerationTimer = 0.0f;

    // Measurements
    private float ramUsage = 0.0f;
    private float vramUsage = 0.0f;
    private float temperature = 0.0f;
    private float humidity = 0.0f;
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
            WindowBorder = WindowBorder.Hidden,
            NumberOfSamples = 8
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
        
        /*
        string generatedMapDirectory = Path.Combine(AppContext.BaseDirectory, "GeneratedMaps");
        Directory.CreateDirectory(generatedMapDirectory);

        BiomeUtility.GenerateBiomeMaps(
            1024,
            1024,
            -0,
            -0,
            120f,
            Path.Combine(generatedMapDirectory, "temperature.png"),
            Path.Combine(generatedMapDirectory, "humidity.png"));

        BiomeUtility.GenerateBiomeAtlasMap(
            4096,
            4096,
            "images/WhittakerAtlas.png",
            Path.Combine(generatedMapDirectory, "biome.png"),
            pixelScale: 30f
        );
        */

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

        _camera = new Camera(new Vector3(3, 40, 3), Vector3.Zero);
        CursorState = CursorState.Grabbed;

        GL.Enable(EnableCap.DepthTest);

        _shader = new Shader("Shaders/main.vert", "Shaders/main.frag");
        _foliageShader = new Shader("Shaders/foliage.vert", "Shaders/foliage.frag");
        _shadowShader = new Shader("Shaders/shadow.vert", "Shaders/shadow.frag");
        _skyShader = new Shader("Shaders/sky.vert", "Shaders/sky.frag");
        _postProcessShader = new Shader("Shaders/postprocess.vert", "Shaders/postprocess.frag");

        _atlas = new TextureAtlas(TextureNames.DiffuseFolder, TextureNames.Names, 48);
        // this needs to be changed to autogenerrate normals
        _normalAtlas = new TextureAtlas(TextureNames.NormalFolder, TextureNames.Names, TextureNames.NormalNames, 256);
        _tintAtlas = new TextureAtlas(TextureNames.TintFolder, TextureNames.Names, TextureNames.TintNames, 48);
        _whittakerAtlas = new TextureAtlas("Images/WhittakerAtlas.png");

        Models.Initialize();
        WorldPregeneration.Pregenerate();

        _shadowCascades = new ShadowMap[CascadeCount];
        for (int i = 0; i < CascadeCount; i++)
            _shadowCascades[i] = new ShadowMap(ShadowMapSize);
        shadowTimer = 1.0f / SHADOW_MAP_RATE;

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        CreateSceneFramebuffer(ClientSize.X, ClientSize.Y);

        _skyVao = GL.GenVertexArray();

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
            Size = new UDim2(0.17f, 0.0f, 0.31f, 0.0f),
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
                        new Label("Temperature : 0.0 °C", "Temperature"),
                        new Label("Humidity : 0.0 mm", "Humidity"),
                    }
                }
                
            },

        };
    }

    private static Matrix4 BuildCascadeLightSpace(
        Vector3 lightDir,
        Matrix4 cameraView,
        Matrix4 cameraProjection,
        float nearDistance,
        float farDistance,
        int mapSize,
        out float texelWorld)
    {
        Matrix4 inverseView = Matrix4.Invert(cameraView);
        Matrix4 inverseProjection = Matrix4.Invert(cameraProjection);
        Vector3[] corners = new Vector3[8];
        int cornerIndex = 0;

        for (int y = -1; y <= 1; y += 2)
        {
            for (int x = -1; x <= 1; x += 2)
            {
                Vector3 nearRay = Vector3.TransformPosition(new Vector3(x, y, -1f), inverseProjection);
                Vector3 farRay = Vector3.TransformPosition(new Vector3(x, y, 1f), inverseProjection);

                Vector3 nearPoint = nearRay * (nearDistance / -nearRay.Z);
                Vector3 farPoint = farRay * (farDistance / -farRay.Z);

                corners[cornerIndex++] = Vector3.TransformPosition(nearPoint, inverseView);
                corners[cornerIndex++] = Vector3.TransformPosition(farPoint, inverseView);
            }
        }

        Vector3 center = Vector3.Zero;
        foreach (Vector3 corner in corners)
            center += corner;
        center /= corners.Length;

        Vector3 up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4 lightView = Matrix4.LookAt(center - lightDir * 1000f, center, up);

        Vector3 lo = new(float.MaxValue);
        Vector3 hi = new(float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector3 lightCorner = Vector3.TransformPosition(corner, lightView);
            lo = Vector3.ComponentMin(lo, lightCorner);
            hi = Vector3.ComponentMax(hi, lightCorner);
        }

        const float xyPadding = 2f;
        const float zPadding = 16f;
        lo.X -= xyPadding;
        lo.Y -= xyPadding;
        hi.X += xyPadding;
        hi.Y += xyPadding;

        texelWorld = MathF.Max(hi.X - lo.X, hi.Y - lo.Y) / mapSize;

        return lightView * Matrix4.CreateOrthographicOffCenter(
            lo.X, hi.X, lo.Y, hi.Y,
            -hi.Z - zPadding, -lo.Z + zPadding);
    }

    private void UpdateCascadeMatrices(Matrix4 cameraView, Matrix4 cameraProjection)
    {
        float nearDistance = NearPlane;

        for (int i = 0; i < CascadeCount; i++)
        {
            _lightSpaces[i] = BuildCascadeLightSpace(
                _lightDir,
                cameraView,
                cameraProjection,
                nearDistance,
                _cascadeSplits[i],
                ShadowMapSize,
                out _texelWorld[i]);

            nearDistance = _cascadeSplits[i];
        }
    }

    private void SetShadowCascadeUniforms(Shader shader)
    {
        shader.SetVector3("cascadeSplits",
            new Vector3(_cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2]));
        shader.SetVector3("cascadeTexelWorlds",
            new Vector3(_texelWorld[0], _texelWorld[1], _texelWorld[2]));

        for (int i = 0; i < CascadeCount; i++)
        {
            TextureUnit textureUnit = (TextureUnit)((int)TextureUnit.Texture1 + i);
            GL.ActiveTexture(textureUnit);
            GL.BindTexture(TextureTarget.Texture2D, _shadowCascades[i].Texture);
            shader.SetInt($"shadowMaps[{i}]", (int)textureUnit - (int)TextureUnit.Texture0);
            shader.SetMatrix4($"lightSpaces[{i}]", _lightSpaces[i]);
        }
    }

    private void CreateSceneFramebuffer(int width, int height)
    {
        if (_sceneFbo != 0)
        {
            GL.DeleteFramebuffer(_sceneFbo);
            GL.DeleteTexture(_sceneColor);
            GL.DeleteTexture(_sceneDepth);
            GL.DeleteTexture(_sceneFoliageMask);
        }

        _sceneFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);

        // Color texture
        _sceneColor = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _sceneColor);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,_sceneColor,0);

        // Mask identifying visible foliage pixels for post-process effects.
        _sceneFoliageMask = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _sceneFoliageMask);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
            TextureTarget.Texture2D, _sceneFoliageMask, 0);

        GL.DrawBuffers(2, new[]
        {
            DrawBuffersEnum.ColorAttachment0,
            DrawBuffersEnum.ColorAttachment1
        });

        // Depth texture (sampled by the FXAA pass)
        _sceneDepth = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _sceneDepth);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
            width, height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _sceneDepth, 0);

        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) !=
            FramebufferErrorCode.FramebufferComplete)
        {
            throw new Exception("Scene framebuffer is incomplete.");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    void RenderShadowMap()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        _shadowShader.Use();
        _shadowShader.SetMatrix4("model", Matrix4.Identity);
        _shadowShader.SetFloat("time", (float)_time);

        _atlas.Bind(TextureUnit.Texture0);
        _shadowShader.SetInt("atlas", 0);

        _normalAtlas.Bind(TextureUnit.Texture1);
        _shadowShader.SetInt("normalAtlas", 1);

        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            GL.Viewport(0, 0, _shadowCascades[cascade].Size, _shadowCascades[cascade].Size);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowCascades[cascade].Fbo);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            _shadowShader.SetMatrix4("lightSpace", _lightSpaces[cascade]);
            lightFrustum.Update(_lightSpaces[cascade]);

            foreach (var mesh in _sceneObjects)
            {
                if (mesh.Active != true)
                    continue;

                _shadowShader.SetInt("isFoliage", mesh.IsFoliage ? 1 : 0);

                Vector3 min = mesh._meshPosition - new Vector3(0.8f);
                Vector3 max = mesh._meshPosition + new Vector3(Chunk.Size - 0.2f);

                if (!lightFrustum.IsBoxVisible(min, max))
                    continue;

                if (mesh.IsFoliage)
                    GL.Disable(EnableCap.CullFace);

                mesh.Draw();

                if (mesh.IsFoliage)
                    GL.Enable(EnableCap.CullFace);
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        ResizeText(e.Width, e.Height);
        if (_sceneFbo != 0 && e.Width > 0 && e.Height > 0)
            CreateSceneFramebuffer(e.Width, e.Height);

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
        {
            _wireframe = !_wireframe;
            GL.PolygonMode(TriangleFace.FrontAndBack, _wireframe ? PolygonMode.Line : PolygonMode.Fill);
        }

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
            _camera.Look(MouseState.Delta, dt);

        chunkUploadTimer += dt;
        chunkGenerationTimer += dt;

        bool uploadChunks = chunkUploadTimer >= CHUNK_UPLOAD_INTERVAL;
        bool generateChunks = chunkGenerationTimer >= CHUNK_GENERATION_INTERVAL;

        if (uploadChunks || generateChunks)
        {
            if (uploadChunks)
                chunkUploadTimer = 0;
            if (generateChunks)
                chunkGenerationTimer = 0;

            DynamicChunks.HandleChunks(uploadChunks, generateChunks);
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

        Matrix4 view = _camera.GetViewMatrix();
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(70f),
            Size.X / (float)Math.Max(Size.Y, 1),
            NearPlane,
            FarPlane);

        shadowTimer += (float)e.Time;
        if(shadowTimer >= 1.0 / SHADOW_MAP_RATE)
        {
            UpdateCascadeMatrices(view, projection);

            shadowTimer = 0;
            RenderShadowMap();
        }

        infoTimer += (float)e.Time;
        if(infoTimer >= 1.0 / INFO_TIMER_RATE)
        {
            infoTimer = 0;
            ramUsage = PerfMonitors.GetRamUsage();
            vramUsage = _vram.GetDedicatedBytes(_time);
            (temperature, humidity) = BiomeUtility.GetBiomeData(_camera.Position.X, _camera.Position.Z);
        }

        if (timeSinceLastFps >= 0.5)
        {
            timeFrame /= countFrame;
            lastFps = timeFrame;
            countFrame = 0;
            timeSinceLastFps = 0;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        Matrix4 model = Matrix4.CreateRotationY(0f);

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);

        // Render procedural sky
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);

        _skyShader.Use();

        _skyShader.SetMatrix4("invProjection", Matrix4.Invert(projection));
        _skyShader.SetMatrix4("invView", Matrix4.Invert(view));
        _skyShader.SetVector3("lightDir", _lightDir);

        GL.BindVertexArray(_skyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);

        /*
        _lightDir = Vector3.Transform(
            _lightDir,
            Quaternion.FromAxisAngle(Vector3.UnitX, 1f * (float)e.Time)
        );
        */

        // Render regular meshes
        _shader.Use();

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        _shader.SetMatrix4("model", model);
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        _shader.SetVector3("lightDir", _lightDir);
        SetShadowCascadeUniforms(_shader);
        _shader.SetVector3("uCamPos", _camera.Position);

        GL.ActiveTexture(TextureUnit.Texture4);
        _atlas.Bind(TextureUnit.Texture4);
        _shader.SetInt("atlas", 4);

        GL.ActiveTexture(TextureUnit.Texture5);
        _normalAtlas.Bind(TextureUnit.Texture5);
        _shader.SetInt("normalAtlas", 5);

        GL.ActiveTexture(TextureUnit.Texture6);
        _tintAtlas.Bind(TextureUnit.Texture6);
        _shader.SetInt("tintAtlas", 6);

        GL.ActiveTexture(TextureUnit.Texture7);
        _whittakerAtlas.Bind(TextureUnit.Texture7);
        _shader.SetInt("whittakerAtlas", 7);

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

        _foliageShader.SetVector3("lightDir", _lightDir);
        SetShadowCascadeUniforms(_foliageShader);
        _foliageShader.SetVector3("uCamPos", _camera.Position);
        _foliageShader.SetFloat("time", (float)_time);

        GL.ActiveTexture(TextureUnit.Texture4);
        _atlas.Bind(TextureUnit.Texture4);
        _foliageShader.SetInt("atlas", 4);

        GL.ActiveTexture(TextureUnit.Texture5);
        _normalAtlas.Bind(TextureUnit.Texture5);
        _foliageShader.SetInt("normalAtlas", 5);

        GL.ActiveTexture(TextureUnit.Texture6);
        _tintAtlas.Bind(TextureUnit.Texture6);
        _foliageShader.SetInt("tintAtlas", 6);

        GL.ActiveTexture(TextureUnit.Texture7);
        _whittakerAtlas.Bind(TextureUnit.Texture7);
        _foliageShader.SetInt("whittakerAtlas", 7);

        GL.Disable(EnableCap.CullFace);

        foreach (var mesh in _sceneObjects)
        {
            if (!mesh.Active || !mesh.IsFoliage)
                continue;

            float distance = Vector3.Distance(_camera.Position, mesh._meshPosition);

            if (distance > 800f)
                continue;

            Vector3 min = mesh._meshPosition - new Vector3(0.8f);
            Vector3 max = mesh._meshPosition + new Vector3(Chunk.Size - 0.2f);

            if (!cameraFrustum.IsBoxVisible(min, max))
                continue;

            drawCalls++;
            mesh.Draw();
                
        }

        if(!_wireframe)
        {
            // FXAA post-processing
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(false);

            _postProcessShader.Use();

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneColor);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _sceneDepth);

            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, _sceneFoliageMask);

            _postProcessShader.SetInt("screenTexture", 0);
            _postProcessShader.SetInt("depthTexture", 1);
            _postProcessShader.SetInt("foliageMask", 2);
            _postProcessShader.SetFloat("inverseWidth", 1.0f / ClientSize.X);
            _postProcessShader.SetFloat("inverseHeight", 1.0f / ClientSize.Y);
            _postProcessShader.SetFloat("nearPlane", NearPlane);
            _postProcessShader.SetFloat("farPlane", FarPlane);
            _postProcessShader.SetMatrix4("projection", projection);
            _postProcessShader.SetMatrix4("inverseProjection", Matrix4.Invert(projection));

            GL.ActiveTexture(TextureUnit.Texture0);

            GL.BindVertexArray(_skyVao);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            // Restore state for UI
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }


        //RenderHud(drawCalls);

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

            var temperatureLabel = _perfMenu.Find("Temperature") as Label;
            temperatureLabel?.Text = $"Temperature : {temperature:F1} °C";

            var humidityLabel = _perfMenu.Find("Humidity") as Label;
            humidityLabel?.Text = $"Humidity : {humidity:F1} mm";

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
        foreach (ShadowMap shadow in _shadowCascades)
            shadow.Dispose();
        _shadowShader.Dispose();
        _postProcessShader.Dispose();

        GL.DeleteFramebuffer(_sceneFbo);
        GL.DeleteTexture(_sceneColor);
        GL.DeleteTexture(_sceneDepth);
        GL.DeleteTexture(_sceneFoliageMask);
        GL.DeleteVertexArray(_skyVao);
        _skyShader.Dispose();
        _foliageShader.Dispose();
        DisposeText();
        base.OnUnload();
    }
}