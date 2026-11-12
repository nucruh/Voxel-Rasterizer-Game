using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

public class Mesh : IDisposable
{
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    public bool IsFoliage = false;

    public Vector3 _meshPosition;

    public bool Active = true;

    public int TriangleCount => _vertexCount / 3;

    public Mesh(float[] vertices, bool isFoliage = false, Vector3 meshPosition = default)
    {
        IsFoliage = isFoliage;
        _vertexCount = vertices.Length / MeshUtility.FloatsPerVertex;
        _meshPosition = meshPosition;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        int stride = MeshUtility.FloatsPerVertex * sizeof(float);

        // 0: position (3)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        // 1: normal (3)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        // 2: uv (2)
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        // 3: tangent (4)
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        // 4: weight (1)
        GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 12 * sizeof(float));
        GL.EnableVertexAttribArray(4);
        // 5: tile rect (4)
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, stride, 13 * sizeof(float));
        GL.EnableVertexAttribArray(5);

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