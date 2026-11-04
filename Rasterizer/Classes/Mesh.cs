using OpenTK.Graphics.OpenGL4;

public class Mesh : IDisposable
{
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    public bool IsFoliage = false;

    public int TriangleCount => _vertexCount / 3;

    public Mesh(float[] vertices, bool isFoliage = false)
    {
        IsFoliage = isFoliage;
        _vertexCount = vertices.Length / 13;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        int stride = 13 * sizeof(float); // 3 for position, 3 for normal, 2 for UV, 4 for tangent

        // Position, normal, UV, tangent
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 12 * sizeof(float));
        GL.EnableVertexAttribArray(4);

        GL.BindVertexArray(0);
    }

    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
