using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;
using static BitmapFontRenderer;

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
    float _rotation;

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
            AddCube(v, new Vector3((c % 2) * 4f, (c / 2) * 4f, 0), cubes[c]);

        AddQuad(v, new Vector3(1, 0, 0), new Vector3(2.4f, 1.2f, -0.3f), stone);

        _mesh = new Mesh(v.ToArray());
    }

    static void AddVertex(List<float> v, Vector3 p, Vector2 uv)
    {
        v.AddRange(new[] { p.X, p.Y, p.Z, 1f, 1f, 1f, uv.X, uv.Y });
    }

    // Upright, unmirrored UV (0..1) for a cube face seen from outside.
    static Vector2 FaceUV(int face, Vector3 p) => face switch
    {
        0 => new Vector2((1 - p.X) / 2, (p.Y + 1) / 2), // back
        1 => new Vector2((p.X + 1) / 2, (p.Y + 1) / 2), // front
        2 => new Vector2((p.Z + 1) / 2, (p.Y + 1) / 2), // left
        3 => new Vector2((1 - p.Z) / 2, (p.Y + 1) / 2), // right
        4 => new Vector2((p.X + 1) / 2, (1 - p.Z) / 2), // top
        _ => new Vector2((p.X + 1) / 2, (p.Z + 1) / 2), // bottom
    };

    void AddCube(List<float> v, Vector3 offset, int[] faceTextures)
    {
        for (int i = 0; i < 36; i++)
        {
            int face = i / 6;
            Vector3 p = new(CubeVertices[i * 3], CubeVertices[i * 3 + 1], CubeVertices[i * 3 + 2]);
            AddVertex(v, p + offset, _atlas.Map(faceTextures[face], FaceUV(face, p)));
        }
    }

    void AddQuad(List<float> v, Vector3 look, Vector3 offset, int texture)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, look));
        Vector3 up = Vector3.Normalize(Vector3.Cross(look, right));

        Vector3[] pos = { -right - up, -right + up, right + up, -right - up, right + up, right - up };
        Vector2[] uv = { new(0, 0), new(0, 1), new(1, 1), new(0, 0), new(1, 1), new(1, 0) };

        for (int i = 0; i < 6; i++)
            AddVertex(v, pos[i] + offset, _atlas.Map(texture, uv[i]));
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

        Matrix4 model = Matrix4.CreateRotationY(_rotation);
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
    static readonly float[] CubeVertices =
    {
        -1,-1,-1,  1,-1,-1,  1, 1,-1,  -1,-1,-1,  1, 1,-1,  -1, 1,-1,
        -1,-1, 1,  1, 1, 1,  1,-1, 1,  -1,-1, 1, -1, 1, 1,   1, 1, 1,
        -1,-1,-1, -1, 1,-1, -1, 1, 1,  -1,-1,-1, -1, 1, 1,  -1,-1, 1,
         1,-1,-1,  1,-1, 1,  1, 1, 1,   1,-1,-1,  1, 1, 1,   1, 1,-1,
        -1, 1,-1,  1, 1,-1,  1, 1, 1,  -1, 1,-1,  1, 1, 1,  -1, 1, 1,
        -1,-1,-1,  1,-1, 1,  1,-1,-1,  -1,-1,-1, -1,-1, 1,   1,-1, 1,
    };
}

class TextureAtlas : IDisposable
{
    const int Tile = 32;

    readonly string[] _names;
    readonly int _cols, _width, _height, _handle;

    public TextureAtlas(string folder, string[] names)
    {
        _names = names;
        _cols = (int)Math.Ceiling(Math.Sqrt(names.Length));
        int rows = (names.Length + _cols - 1) / _cols;
        _width = _cols * Tile;
        _height = rows * Tile;

        byte[] pixels = new byte[_width * _height * 4];

        for (int i = 0; i < names.Length; i++)
        {
            using var stream = File.OpenRead(Path.Combine(folder, names[i] + ".png"));
            byte[] tile = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data;

            int ox = (i % _cols) * Tile, oy = (i / _cols) * Tile;

            for (int y = 0; y < Tile; y++)
                System.Buffer.BlockCopy(tile, y * Tile * 4, pixels, ((oy + y) * _width + ox) * 4, Tile * 4);
        }

        _handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _handle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, _width, _height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
    }

    public int Id(string name) => Array.IndexOf(_names, name);

    // local (0,0) = bottom-left of the face, (1,1) = top-right
    public Vector2 Map(int id, Vector2 local)
    {
        float e = 0.01f / Tile; // keep samples inside the tile
        local = new Vector2(e + local.X * (1 - 2 * e), e + local.Y * (1 - 2 * e));

        int col = id % _cols, row = id / _cols;

        return new Vector2(
            (col + local.X) * Tile / _width,
            (row + 1 - local.Y) * Tile / _height);
    }

    public void Bind()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose() => GL.DeleteTexture(_handle);
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

