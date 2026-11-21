
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

public class TextureAtlas : IDisposable
{
    private struct TextureRegion
    {
        public int X, Y, Width, Height;
    }

    private readonly int _tile;
    private readonly int _width, _height, _handle;
    private readonly string[] _names;
    private readonly TextureRegion[] _regions;

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
        _regions = new TextureRegion[names.Length];

        _nameToId = names
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index);

        int cols = (int)Math.Ceiling(Math.Sqrt(names.Length));
        int rows = (names.Length + cols - 1) / cols;

        _width = cols * _tile;
        _height = rows * _tile;

        byte[] pixels = new byte[_width * _height * 4];

        for (int i = 0; i < names.Length; i++)
        {
            using var stream = File.OpenRead(Path.Combine(folder, sourceNames[i] + ".png"));
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            if (image.Width > _tile || image.Height > _tile)
                throw new ArgumentException($"Texture '{sourceNames[i]}' exceeds the maximum atlas cell size of {_tile}x{_tile}.");

            int ox = (i % cols) * _tile;
            int oy = (i / cols) * _tile;

            _regions[i] = new TextureRegion
            {
                X = ox,
                Y = oy,
                Width = image.Width,
                Height = image.Height
            };

            for (int y = 0; y < image.Height; y++)
                System.Buffer.BlockCopy(image.Data, y * image.Width * 4, pixels,
                    ((oy + y) * _width + ox) * 4, image.Width * 4);
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
        var region = _regions[id];

        const float inset = 0.01f;

        local = new Vector2(
            inset + local.X * (1 - 2 * inset),
            inset + local.Y * (1 - 2 * inset)
        );

        return new Vector2(
            (region.X + local.X * region.Width) / _width,
            (region.Y + (1 - local.Y) * region.Height) / _height
        );
    }

    public void Bind(TextureUnit textureUnit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(textureUnit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    // single image loading constructor (for non-atlas textures)
    public TextureAtlas(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        _width = image.Width;
        _height = image.Height;
        _tile = 0;
        _names = Array.Empty<string>();
        _regions = Array.Empty<TextureRegion>();
        _nameToId = new Dictionary<string, int>();

        _handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _handle);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            image.Width,
            image.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            image.Data
        );
    }

    public void Dispose() => GL.DeleteTexture(_handle);
}
