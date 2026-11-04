using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

public class TextureAtlas : IDisposable
{
    private int Tile = 32;

    readonly string[] _names;
    readonly Dictionary<string, List<string>> _nameToNames;
    readonly int _cols, _width, _height, _handle;

    public TextureAtlas(string folder, string[] names)
    {
        _names = names;
        _nameToNames = TextureNames.NameToNames;

        _cols = (int)Math.Ceiling(Math.Sqrt(names.Length));
        int rows = (names.Length + _cols - 1) / _cols;
        _width = _cols * Tile;
        _height = rows * Tile;

        Console.WriteLine(_cols);

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

    public TextureAtlas(string folder, string[] names, int texSize)
    {
        Tile = texSize;
        _names = names;
        _nameToNames = TextureNames.NameToNames2;

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
    public Vector2 Map(string name, Vector2 local, Vector3? look = null)
    {
        string textureName = name;

        if (look.HasValue)
        {
            Vector3 normal = look.Value;

            int face = normal.X > 0.5f ? 0 :
                       normal.X < -0.5f ? 1 :
                       normal.Y > 0.5f ? 2 :
                       normal.Y < -0.5f ? 3 :
                       normal.Z > 0.5f ? 4 : 5;

            textureName = _nameToNames[name][face];
        }

        int id = Id(textureName);

        float e = 0.01f / Tile;
        local = new Vector2(e + local.X * (1 - 2 * e), e + local.Y * (1 - 2 * e));

        int col = id % _cols, row = id / _cols;

        return new Vector2(
            (col + local.X) * Tile / _width,
            (row + 1 - local.Y) * Tile / _height);
    }

    public void Bind(TextureUnit textureUnit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(textureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose() => GL.DeleteTexture(_handle);
}