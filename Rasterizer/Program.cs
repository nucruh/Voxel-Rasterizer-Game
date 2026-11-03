using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using static BitmapFontRenderer;
using static MeshUtility;

using GameWindow = OpenTK.Windowing.Desktop.GameWindow;

using var game = new Game();
game.Run();

class Game : GameWindow
{
    Shader _shader = null!;
    List<Mesh> _sceneObjects = new List<Mesh>();
    Camera _camera = null!;
    TextureAtlas _atlas = null!;

    // --- shadow mapping ---
    const int ShadowMapSize = 4096;
    const int WorldChunks = 8;
    const float WorldHeight = 64f;   // replace with your max terrain height

    Shader _shadowShader = null!;
    ShadowMap _shadow = null!;
    Matrix4 _lightSpace;
    Vector3 _lightDir = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f)); // direction the light TRAVELS
    float _texelWorld;

    Shader _skyShader = null!;
    int _skyVao;

    public Game() : base(
        GameWindowSettings.Default,
        new NativeWindowSettings
        {
            ClientSize = new Vector2i(3440, 1440),
            Location = SystemInfo.screenCenter - new Vector2i(3440 / 2, 1440 / 2),
            Title = "Rasterizer",
            APIVersion = new Version(4, 3),
            Profile = ContextProfile.Core,
            Vsync = VSyncMode.Off,
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

        InitTextRendering(ClientSize.X, ClientSize.Y);

        _camera = new Camera(new Vector3(3, 2, 4), Vector3.Zero);
        CursorState = CursorState.Grabbed;

        GL.Enable(EnableCap.DepthTest);

        _shader = new Shader(
            File.ReadAllText("Shaders/main.vert"),
            File.ReadAllText("Shaders/main.frag"));

        _atlas = new TextureAtlas("Textures", TextureNames.Names);

        for (int x = 0; x < WorldChunks; x++)
            for (int z = 0; z < WorldChunks; z++)
            {
                Chunk c = new Chunk(new Vector3(x * Chunk.Size, 0, z * Chunk.Size));
                c.Generate();
                c.CreateMesh(_atlas);

                _sceneObjects.Add(c.mesh!);
            }

        // --- shadow setup (world is static, so the map is rendered once) ---
        _shadowShader = new Shader(
            File.ReadAllText("Shaders/shadow.vert"),
            File.ReadAllText("Shaders/shadow.frag"));

        _shadow = new ShadowMap(ShadowMapSize);

        _skyShader = new Shader(
            File.ReadAllText("Shaders/sky.vert"),
            File.ReadAllText("Shaders/sky.frag"));

        _skyVao = GL.GenVertexArray();

        // maybe lowering this can help in needing less resolution for the shadowmap
        float worldW = WorldChunks * Chunk.Size;
        _lightSpace = BuildLightSpaceFit(
            _lightDir,
            new Vector3(0, 0, 0),
            new Vector3(worldW, WorldHeight, worldW),
            ShadowMapSize,
            out _texelWorld);

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

        foreach (var mesh in _sceneObjects)
            mesh.Draw();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);

        if (KeyboardState.IsKeyDown(Keys.Escape))
            Close();

        if (!IsFocused)
            return;

        float dt = (float)e.Time;
        float speed = _camera.Speed * (KeyboardState.IsKeyDown(Keys.LeftControl) ? 3f : 1f);

        Vector3 flatFront = Vector3.Normalize(new Vector3(_camera.Front.X, 0, _camera.Front.Z));
        Vector3 move = Vector3.Zero;

        if (KeyboardState.IsKeyDown(Keys.W)) move += _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= _camera.Front;
        if (KeyboardState.IsKeyDown(Keys.D)) move += _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.A)) move -= _camera.Right;
        if (KeyboardState.IsKeyDown(Keys.Space)) move += Vector3.UnitY;
        if (KeyboardState.IsKeyDown(Keys.LeftShift)) move -= Vector3.UnitY;

        if (move != Vector3.Zero)
            _camera.Position += Vector3.Normalize(move) * speed * dt;

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

        // Render scene
        _shader.Use();

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Cw);

        _shader.SetMatrix4("model", model);
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        // shadow uniforms
        _shader.SetMatrix4("lightSpace", _lightSpace);
        _shader.SetVector3("lightDir", _lightDir);
        _shader.SetFloat("texelWorld", _texelWorld);
        _shader.SetVector3("uCamPos", _camera.Position);

        // shadow map on unit 1, atlas on unit 0
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _shadow.Texture);
        _shader.SetInt("shadowMap", 1);

        GL.ActiveTexture(TextureUnit.Texture0);
        _atlas.Bind();
        _shader.SetInt("atlas", 0);

        foreach (var mesh in _sceneObjects)
            mesh.Draw();

        float marginScaleX = (float)(GlyphPixelW + TextScale) / ClientSize.X;
        float marginScaleY = (float)(GlyphPixelH + TextScale * 2) / ClientSize.Y;

        string data = $"FPS : {Math.Round(lastFps, 0)}\n" +
            $"Triangles : {GetTotalTriangles(_sceneObjects.ToArray()) / 1000:f1}k\n" +
            $"Draw calls: {_sceneObjects.Count}\n" +
            $"Screen : {ClientSize.X} x {ClientSize.Y}";

        // 2D overlay state
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        RenderText(data, marginScaleX, marginScaleY, white);

        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);

        UiRenderQueue.Flush();

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        foreach (var mesh in _sceneObjects)
            mesh.Dispose();
        _atlas.Dispose();
        _shader.Dispose();
        _shadow.Dispose();
        _shadowShader.Dispose();
        GL.DeleteVertexArray(_skyVao);
        _skyShader.Dispose();
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