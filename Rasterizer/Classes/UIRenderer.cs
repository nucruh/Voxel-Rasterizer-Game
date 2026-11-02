using OpenTK.Graphics.OpenGL;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

/// <summary>
/// Renders text using a custom 5x5 pixel bitmap font atlas (images/font.png, 50x50px).
///
/// Atlas layout (each char = 5x5 px, 10 chars per row):
///   Row 0: A B C D E F G H I J
///   Row 1: K L M N O P Q R S T
///   Row 2: U V W X Y Z
///   Row 3: 0 1 2 3 4 5 6 7 8 9
///   Row 4: : . / - +
///   Space: bottom-right corner (col 9, row 4) — treated as blank
///
/// Scaling: integer multiples of 5 pixels (e.g. scale=1 → 5px, scale=2 → 10px …)
/// Shadow: 1 pixel right + 1 pixel down (in 5px base units, scaled correctly)
/// </summary>
public static class BitmapFontRenderer
{
    // ── Atlas constants ────────────────────────────────────────────────────────
    private const int CHAR_W = 5;   // source pixels per glyph
    private const int CHAR_H = 5;
    private const int ATLAS_COLS = 10;
    private const int ATLAS_ROWS = 5;
    private const int ATLAS_PX = 50; // atlas is 50×50

    public struct TextColor
    {
        public int r, g, b;

        public TextColor(int r, int g, int b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public TextColor()
        {
            this.r = 255; this.g = 255; this.b = 255;
        }
    }

    public static readonly TextColor white = new TextColor(240, 240, 240);

    // Map from char → (col, row) in the atlas
    private static (int col, int row)? GetGlyphCoord(char c) => c switch
    {
        // Row 0 – A..J
        'A' => (0, 0),
        'B' => (1, 0),
        'C' => (2, 0),
        'D' => (3, 0),
        'E' => (4, 0),
        'F' => (5, 0),
        'G' => (6, 0),
        'H' => (7, 0),
        'I' => (8, 0),
        'J' => (9, 0),
        // Row 1 – K..T
        'K' => (0, 1),
        'L' => (1, 1),
        'M' => (2, 1),
        'N' => (3, 1),
        'O' => (4, 1),
        'P' => (5, 1),
        'Q' => (6, 1),
        'R' => (7, 1),
        'S' => (8, 1),
        'T' => (9, 1),
        // Row 2 – U..Z (only 6 glyphs)
        'U' => (0, 2),
        'V' => (1, 2),
        'W' => (2, 2),
        'X' => (3, 2),
        'Y' => (4, 2),
        'Z' => (5, 2),
        // Row 3 – 0..9
        '0' => (0, 3),
        '1' => (1, 3),
        '2' => (2, 3),
        '3' => (3, 3),
        '4' => (4, 3),
        '5' => (5, 3),
        '6' => (6, 3),
        '7' => (7, 3),
        '8' => (8, 3),
        '9' => (9, 3),
        // Row 4 – punctuation
        ':' => (0, 4),
        '.' => (1, 4),
        ',' => (1, 4), // map comma and dot to same thingamajig
        '/' => (2, 4),
        '-' => (3, 4),
        '+' => (4, 4),
        '%' => (5, 4),
        '°' => (6, 4),
        '_' => (7, 4),
        '>' => (8, 4),
        '<' => (9, 4),
        '=' => (0, 5),
        '(' => (1, 5),
        ')' => (2, 5),
        '[' => (3, 5),
        ']' => (4, 5),
        '{' => (5, 5),
        '}' => (6, 5),
        '?' => (7, 5),
        '!' => (8, 5),
        '~' => (9, 5),
        // Space / fallback – use bottom-right corner (blank)
        ' ' => (8, 9),
        _ => (9, 9)   //  x
    };

    // ── GL state ───────────────────────────────────────────────────────────────
    private static int _atlasTexture;
    private static int _program, _vao, _vbo;

    // ── Window ─────────────────────────────────────────────────────────────────
    private static int _windowWidth;
    private static int _windowHeight;

    // ── Font size ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Scale factor. 1 = 5 px glyphs, 2 = 10 px, etc.
    /// Set/change via <see cref="SetScale"/> or <see cref="Resize"/>.
    /// </summary>
    public static int TextScale { get; private set; } = 2;

    // Derived pixel sizes
    public static int GlyphPixelW => CHAR_W * TextScale;
    public static int GlyphPixelH => CHAR_H * TextScale;

    // ── Public API ─────────────────────────────────────────────────────────────

    public static void InitTextRendering(int windowWidth, int windowHeight, int scale = 2)
    {
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
        TextScale = Math.Max(1, scale);

        LoadAtlasTexture("Images/font.png");
        BuildShaderProgram();
        BuildMesh();
    }

    /// <summary>Change the glyph scale (must be a positive integer).</summary>
    public static void SetTextScale(int scale) => TextScale = Math.Max(1, scale);

    /// <summary>
    /// Call when the window is resized.
    /// Optionally pass a new scale; omit (or pass 0) to auto-compute one that
    /// stays proportional to the window height (same heuristic as the old code).
    /// </summary>
    public static void ResizeText(int newWidth, int newHeight, int newScale = 0)
    {
        _windowWidth = newWidth;
        _windowHeight = newHeight;

        if (newScale > 0)
            TextScale = newScale;
        else
            TextScale = Math.Max(1, (int)Math.Round(newHeight / 270f)); // ~1 per 270px

        GL.UseProgram(_program);
        GL.Uniform2(GL.GetUniformLocation(_program, "uScreenSize"),
                    (float)_windowWidth, (float)_windowHeight);
        GL.UseProgram(0);
    }

    /// <summary>
    /// Returns the rendered height (in pixels) of a single line of text at the
    /// current scale.
    /// </summary>
    public static float GetLineHeight() => GlyphPixelH;

    /// <summary>
    /// Measures the rendered size of a (possibly multi-line) string.
    /// </summary>
    public static (float width, float height) Measure(string text)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);

        string[] lines = text.Split('\n');
        int maxCols = 0;
        foreach (var line in lines)
            maxCols = Math.Max(maxCols, line.Length);

        float w = maxCols * GlyphPixelW;
        float h = lines.Length * GlyphPixelH;
        return (w, h);
    }

    /// <summary>
    /// Render a string.
    /// </summary>
    /// <param name="text">Text to draw (supports '\n').</param>
    /// <param name="xScale">Horizontal position as a fraction of the window width [0..1].</param>
    /// <param name="yScale">Vertical position as a fraction of the window height [0..1].</param>
    /// <param name="color">TextColor component RGB, 0 - 255</param>
    public static void RenderText(string text, float xScale, float yScale,
                                  TextColor color, int zIndex = 0)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Capture everything that can change before the flush
        int scale = TextScale;
        float originX = xScale * _windowWidth;
        float originY = yScale * _windowHeight;

        UiRenderQueue.Submit(zIndex, () => DrawTextNow(text, originX, originY, color, scale));
    }

    private static void DrawTextNow(string text, float originX, float originY,
                                TextColor color, int scale)
    {
        int prevScale = TextScale;
        TextScale = scale;

        float r = Math.Clamp(color.r / 255f, 0f, 1f);
        float g = Math.Clamp(color.g / 255f, 0f, 1f);
        float b = Math.Clamp(color.b / 255f, 0f, 1f);

        float shadow = TextScale;
        string[] lines = text.Split('\n');

        BuildAndDrawText(lines, originX + shadow, originY + shadow, 0f, 0f, 0f, 1f);
        BuildAndDrawText(lines, originX, originY, r, g, b, 1f);

        TextScale = prevScale;
    }


    // ── Internals ──────────────────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Interoperability", "CA1416:Validate platform compatibility", Justification = "GDI+ used only to load PNG")]
    private static void LoadAtlasTexture(string path)
    {
        using var bmp = new Bitmap(path);

        // Flip for OpenGL (bottom-left origin)
        bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

        BitmapData data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        _atlasTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _atlasTexture);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            bmp.Width, bmp.Height, 0,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            PixelType.UnsignedByte, data.Scan0);

        bmp.UnlockBits(data);

        // Nearest-neighbour so the pixel font stays crisp at all scales
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                        (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                        (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                        (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                        (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static void BuildShaderProgram()
    {
        _program = CreateProgram("shaders/text.vert", "shaders/text.frag");

        GL.UseProgram(_program);
        GL.Uniform1(GL.GetUniformLocation(_program, "uTexture"), 0);
        GL.Uniform2(GL.GetUniformLocation(_program, "uScreenSize"),
                    (float)_windowWidth, (float)_windowHeight);
        GL.UseProgram(0);
    }

    private static void BuildMesh()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int stride = sizeof(float) * 4; // x, y, u, v

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, sizeof(float) * 2);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Builds one big vertex buffer covering every visible glyph across all
    /// lines, then submits a single draw call.  This keeps the per-frame work
    /// minimal even for longer strings.
    /// </summary>
    private static void BuildAndDrawText(string[] lines,
                                   float originX, float originY,
                                   float r, float g, float b, float a)
    {
        // 6 vertices × 4 floats (x,y,u,v) per glyph
        // We'll count actual glyphs first to size the array exactly.
        int totalGlyphs = 0;
        foreach (var line in lines)
            totalGlyphs += line.Length; // includes spaces; they get a valid UV too

        if (totalGlyphs == 0) return;

        float[] verts = new float[totalGlyphs * 6 * 4];
        int vi = 0;

        float atlasW = ATLAS_PX;
        float atlasH = ATLAS_PX;

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            // Y increases downward in screen space; our shader maps screen → NDC.
            // originY is the TOP of the text block.
            float py = originY + lineIdx * (GlyphPixelH + TextScale * 2);

            for (int charIdx = 0; charIdx < line.Length; charIdx++)
            {
                char c = char.ToUpper(line[charIdx]);
                var coord = GetGlyphCoord(c) ?? (9, 4); // unknown → space

                float px = originX + charIdx * (GlyphPixelW + TextScale); // pad w/ 1

                // UV in atlas (remember: atlas is already flipped for GL)
                // After the Y-flip the row numbering is reversed:
                //   source row 0 (top) → GL row (ATLAS_ROWS-1 - 0)
                int totalPhysicalRows = (int)(atlasH / CHAR_H); // 50 / 5 = 10
                int atlasRow = (totalPhysicalRows - 1) - coord.row;
                float u0 = (coord.col * CHAR_W) / atlasW;
                float u1 = u0 + CHAR_W / atlasW;
                float v0 = (atlasRow * CHAR_H) / atlasH;       // bottom of glyph in GL coords
                float v1 = v0 + CHAR_H / atlasH;               // top   of glyph in GL coords

                float x0 = px;
                float y0 = py;
                float x1 = px + GlyphPixelW;
                float y1 = py + GlyphPixelH;

                // Two triangles, CCW
                //  TL=(x0,y0,u0,v1)  TR=(x1,y0,u1,v1)
                //  BL=(x0,y1,u0,v0)  BR=(x1,y1,u1,v0)
                // Triangle 1: TL, BL, BR
                verts[vi++] = x0; verts[vi++] = y0; verts[vi++] = u0; verts[vi++] = v1;
                verts[vi++] = x0; verts[vi++] = y1; verts[vi++] = u0; verts[vi++] = v0;
                verts[vi++] = x1; verts[vi++] = y1; verts[vi++] = u1; verts[vi++] = v0;
                // Triangle 2: TL, BR, TR
                verts[vi++] = x1; verts[vi++] = y1; verts[vi++] = u1; verts[vi++] = v0;
                verts[vi++] = x1; verts[vi++] = y0; verts[vi++] = u1; verts[vi++] = v1;
                verts[vi++] = x0; verts[vi++] = y0; verts[vi++] = u0; verts[vi++] = v1;
            }
        }

        // Upload & draw

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vi * sizeof(float), verts, BufferUsageHint.StreamDraw);

        GL.UseProgram(_program);
        GL.BindVertexArray(_vao);

        GL.Uniform4(GL.GetUniformLocation(_program, "uColor"), r, g, b, a);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _atlasTexture);

        GL.DrawArrays(PrimitiveType.Triangles, 0, vi / 4); // 4 floats per vertex

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
    }

    // ── Shader helpers ─────────────────────────────────────────────────────────

    private static int CreateProgram(string vertPath, string fragPath)
    {
        int vert = CompileShader(vertPath, ShaderType.VertexShader);
        int frag = CompileShader(fragPath, ShaderType.FragmentShader);

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vert);
        GL.AttachShader(prog, frag);
        GL.LinkProgram(prog);

        GL.DeleteShader(vert);
        GL.DeleteShader(frag);

        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0)
            throw new Exception($"Shader link failed: {GL.GetProgramInfoLog(prog)}");

        return prog;
    }

    private static int CompileShader(string path, ShaderType type)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, File.ReadAllText(path));
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
            throw new Exception($"Shader compile failed ({path}): {GL.GetShaderInfoLog(shader)}");

        return shader;
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public static void DisposeText()
    {
        if (_atlasTexture != 0) GL.DeleteTexture(_atlasTexture);
        if (_vbo != 0) GL.DeleteBuffer(_vbo);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_program != 0) GL.DeleteProgram(_program);
    }
}

public static class ImageRenderer
{

}