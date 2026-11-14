using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;
public class TextureAtlas : IDisposable
{
    private readonly int _tile;
    private readonly int _cols, _width, _height, _handle;
    private readonly string[] _names;

    private readonly Dictionary<string, int> _nameToId;

    public TextureAtlas(string folder, string[] names, int texSize = 32)
        : this(folder, names, names, texSize)
    {
    }

    public TextureAtlas(string folder, string[] names, string[] sourceNames, int texSize = 32)
    {
        if (names.Length != sourceNames.Length)
            throw new ArgumentException("Logical atlas names and source names must have the same length.");

        _tile = texSize;
        _names = names;

        _nameToId = names
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index);

        _cols = (int)Math.Ceiling(Math.Sqrt(names.Length));
        int rows = (names.Length + _cols - 1) / _cols;

        _width = _cols * _tile;
        _height = rows * _tile;

        byte[] pixels = new byte[_width * _height * 4];

        for (int i = 0; i < names.Length; i++)
        {
            using var stream = File.OpenRead(Path.Combine(folder, sourceNames[i] + ".png"));
            byte[] tile = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data;

            int ox = (i % _cols) * _tile;
            int oy = (i / _cols) * _tile;

            for (int y = 0; y < _tile; y++)
                System.Buffer.BlockCopy(tile, y * _tile * 4, pixels,
                    ((oy + y) * _width + ox) * 4, _tile * 4);
        }

        _handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _handle);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            _width, _height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
    }

    public int Id(string name) => _nameToId[name];

    public Vector2 Map(string name, Vector2 local, Vector3? look = null)
    {
        return MapTexture(name, local, look, false);
    }

    public Vector2 MapNormal(string name, Vector2 local, Vector3? look = null)
    {
        return MapTexture(name, local, look, true);
    }

    private Vector2 MapTexture(string name, Vector2 local, Vector3? look, bool normal)
    {
        string textureName = name;

        if (look.HasValue)
        {
            Vector3 n = look.Value;

            int face = n.X > 0.5f ? 0 :
                       n.X < -0.5f ? 1 :
                       n.Y > 0.5f ? 2 :
                       n.Y < -0.5f ? 3 :
                       n.Z > 0.5f ? 4 : 5;

            var block = BlockRegistry.Blocks[BlockRegistry.NameToId[name]];

            textureName = normal
                ? block.NormalMaps[face]
                : block.Textures[face];
        }

        int id = Id(textureName);

        float e = 0.01f / _tile;

        local = new Vector2(
            e + local.X * (1 - 2 * e),
            e + local.Y * (1 - 2 * e));

        int col = id % _cols;
        int row = id / _cols;

        return new Vector2(
            (col + local.X) * _tile / _width,
            (row + 1 - local.Y) * _tile / _height);
    }

    public void Bind(TextureUnit textureUnit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(textureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose() => GL.DeleteTexture(_handle);
}