#pragma warning disable CA1416
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

public static class ImageRenderer
{
    private static int _program, _vao, _vbo;
    private static readonly Dictionary<string, int> _cache = new();

    public static void Init(int windowWidth, int windowHeight)
    {
        _program = BitmapFontRenderer.CreateProgram("shaders/image.vert", "shaders/image.frag");
        GL.UseProgram(_program);
        GL.Uniform1(GL.GetUniformLocation(_program, "uTexture"), 0);
        GL.UseProgram(0);
        Resize(windowWidth, windowHeight);

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * 24, IntPtr.Zero, BufferUsageHint.StreamDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, sizeof(float) * 4, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 4, sizeof(float) * 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public static void Resize(int w, int h)
    {
        GL.UseProgram(_program);
        GL.Uniform2(GL.GetUniformLocation(_program, "uScreenSize"), (float)w, (float)h);
        GL.UseProgram(0);
    }

    /// <summary>Loads a texture once and caches it by path. Returns the GL texture id.</summary>
    public static int Load(string path, bool pixelArt = true)
    {
        if (_cache.TryGetValue(path, out int id)) return id;

        using var bmp = new Bitmap(path);   // NOT flipped: row 0 = top, matches screen space
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            bmp.Width, bmp.Height, 0,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
        bmp.UnlockBits(data);

        var filter = pixelArt ? TextureMinFilter.Nearest : TextureMinFilter.Linear;
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)filter);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        _cache[path] = id;
        return id;
    }

    /// <summary>Queues an image. u0,v0,u1,v1 let you draw a sub-rectangle (sprite sheet / atlas).</summary>
    public static void Render(int texture, float x, float y, float w, float h,
                              BitmapFontRenderer.TextColor tint, float alpha, int zIndex,
                              float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
    {
        if (w <= 0 || h <= 0) return;
        float r = tint.r / 255f, g = tint.g / 255f, b = tint.b / 255f;
        UiRenderQueue.Submit(zIndex, () => DrawNow(texture, x, y, w, h, r, g, b, alpha, u0, v0, u1, v1));
    }

    private static void DrawNow(int tex, float x, float y, float w, float h,
                                float r, float g, float b, float a,
                                float u0, float v0, float u1, float v1)
    {
        float[] v =
        {
            x,     y,     u0, v0,
            x,     y + h, u0, v1,
            x + w, y + h, u1, v1,
            x + w, y + h, u1, v1,
            x + w, y,     u1, v0,
            x,     y,     u0, v0,
        };

        GL.UseProgram(_program);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, sizeof(float) * v.Length, v);
        GL.Uniform4(GL.GetUniformLocation(_program, "uTint"), r, g, b, a);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    public static void Dispose()
    {
        foreach (var t in _cache.Values) GL.DeleteTexture(t);
        _cache.Clear();
        if (_vbo != 0) GL.DeleteBuffer(_vbo);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_program != 0) GL.DeleteProgram(_program);
    }
}