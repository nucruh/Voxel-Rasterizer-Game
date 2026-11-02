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
    // Index in this list = texture id. Loads Textures/<name>.png (32x32).
    static readonly string[] TextureNames = { "stone"};

    Shader _shader = null!;
    Mesh _mesh = null!;
    Camera _camera = null!;
    TextureAtlas _atlas = null!;
    //float _rotation;

    public Game() : base(
        GameWindowSettings.Default,
        new NativeWindowSettings
        {
            ClientSize = new Vector2i(1280, 720),
            Title = "Rasterizer",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Vsync = VSyncMode.On,
        })
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        InitTextRendering(ClientSize.X, ClientSize.Y);

        _camera = new Camera(new Vector3(3, 2, 4), Vector3.Zero);
        CursorState = CursorState.Grabbed;

        GL.ClearColor(0.08f, 0.09f, 0.12f, 1f);
        GL.Enable(EnableCap.DepthTest);

        _shader = new Shader(
            File.ReadAllText("Shaders/main.vert"),
            File.ReadAllText("Shaders/main.frag"));

        _atlas = new TextureAtlas("Textures", TextureNames);

        int stone = _atlas.Id("stone"), dirt = _atlas.Id("dirt"), planks = _atlas.Id("planks");
        int grassTop = _atlas.Id("grass_top"), grassSide = _atlas.Id("grass_side");

        // Face order: Back, Front, Left, Right, Top, Bottom
        int[][] cubes =
        {
            new[] { stone, stone, stone, stone, stone, stone },
            new[] { dirt, dirt, dirt, dirt, dirt, dirt },
            new[] { grassSide, grassSide, grassSide, grassSide, grassTop, dirt },
            new[] { planks, planks, planks, planks, planks, planks },
        };

        var v = new List<float>();

        for (int c = 0; c < 4; c++)
            AddCube(v, new Vector3((c % 2) * 4f, (c / 2) * 4f, 0), cubes[c], _atlas);

        AddQuad(v, new Vector3(1, 0, 0), new Vector3(2.4f, 1.2f, -0.3f), stone, _atlas);

        _mesh = new Mesh(v.ToArray());
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

        //_rotation += (float)e.Time;

        if (!IsFocused)
            return;

        float dt = (float)e.Time;
        float speed = _camera.Speed * (KeyboardState.IsKeyDown(Keys.LeftControl) ? 3f : 1f);

        Vector3 flatFront = Vector3.Normalize(new Vector3(_camera.Front.X, 0, _camera.Front.Z));
        Vector3 move = Vector3.Zero;

        if (KeyboardState.IsKeyDown(Keys.W)) move += flatFront;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= flatFront;
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
            MathHelper.DegreesToRadians(60f),
            Size.X / (float)Math.Max(Size.Y, 1),
            0.1f,
            100f);

        _shader.SetMatrix4("model", model);
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        _atlas.Bind();
        _shader.SetInt("atlas", 0);

        _mesh.Draw();

        float marginScaleX = (float)(GlyphPixelW + TextScale) / ClientSize.X;
        float marginScaleY = (float)(GlyphPixelH + TextScale * 2) / ClientSize.Y;

        string data = $"FPS : {Math.Round(lastFps, 0)}\n" +
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
        _mesh.Dispose();
        _atlas.Dispose();
        _shader.Dispose();
        base.OnUnload();
    }

    // Positions only. Face order: Back, Front, Left, Right, Top, Bottom

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

    public void SetInt(string name, int value) => GL.Uniform1(GL.GetUniformLocation(_program, name), value);

    public void Dispose() => GL.DeleteProgram(_program);
}

