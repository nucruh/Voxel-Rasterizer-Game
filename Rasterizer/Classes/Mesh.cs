using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

public class Mesh : IDisposable
{
    // Position remains full precision. The other attributes are packed as:
    // normal = 3 signed normalized bytes + 1 padding byte
    // UV = 2 half floats
    // tangent = 3 signed normalized bytes + handedness byte
    // weight = half float
    // tile rect = 4 half floats
    // biome data = one unsigned short containing two byte values
    private const int PositionOffset = 0;
    private const int NormalOffset = 12;
    private const int UvOffset = 16;
    private const int TangentOffset = 20;
    private const int WeightOffset = 24;
    private const int TileRectOffset = 26;
    private const int BiomeDataOffset = 34;
    private const int PackedStride = 36;

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
        byte[] packedVertices = PackVertices(vertices);
        GL.BufferData(BufferTarget.ArrayBuffer, packedVertices.Length, packedVertices, BufferUsageHint.StaticDraw);

        int stride = PackedStride;

        // 0: position (3)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        // 1: normal (3)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Byte, true, stride, NormalOffset);
        GL.EnableVertexAttribArray(1);
        // 2: uv (2)
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.HalfFloat, false, stride, UvOffset);
        GL.EnableVertexAttribArray(2);
        // 3: tangent (4)
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Byte, true, stride, TangentOffset);
        GL.EnableVertexAttribArray(3);
        // 4: weight (1)
        GL.VertexAttribPointer(4, 1, VertexAttribPointerType.HalfFloat, false, stride, WeightOffset);
        GL.EnableVertexAttribArray(4);
        // 5: tile rect (4)
        GL.VertexAttribPointer(5, 4, VertexAttribPointerType.HalfFloat, false, stride, TileRectOffset);
        GL.EnableVertexAttribArray(5);
        // 6: packed biome data (two unsigned byte values)
        GL.VertexAttribIPointer(6, 1, VertexAttribIntegerType.UnsignedShort, stride, BiomeDataOffset);
        GL.EnableVertexAttribArray(6);

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

    private static byte[] PackVertices(float[] vertices)
    {
        int vertexCount = vertices.Length / MeshUtility.FloatsPerVertex;
        byte[] packed = new byte[vertexCount * PackedStride];

        for (int i = 0; i < vertexCount; i++)
        {
            int source = i * MeshUtility.FloatsPerVertex;
            int destination = i * PackedStride;

            // Position stays as three full-precision floats.
            System.Buffer.BlockCopy(vertices, (source + 0) * sizeof(float), packed, destination + PositionOffset, 12);

            // Unit vectors use signed normalized bytes.
            packed[destination + NormalOffset + 0] = ToSnormByte(vertices[source + 3]);
            packed[destination + NormalOffset + 1] = ToSnormByte(vertices[source + 4]);
            packed[destination + NormalOffset + 2] = ToSnormByte(vertices[source + 5]);

            WriteHalf(packed, destination + UvOffset, vertices[source + 6]);
            WriteHalf(packed, destination + UvOffset + 2, vertices[source + 7]);

            packed[destination + TangentOffset + 0] = ToSnormByte(vertices[source + 8]);
            packed[destination + TangentOffset + 1] = ToSnormByte(vertices[source + 9]);
            packed[destination + TangentOffset + 2] = ToSnormByte(vertices[source + 10]);
            packed[destination + TangentOffset + 3] = ToSnormByte(vertices[source + 11]);

            WriteHalf(packed, destination + WeightOffset, vertices[source + 12]);
            for (int component = 0; component < 4; component++)
                WriteHalf(packed, destination + TileRectOffset + component * 2, vertices[source + 13 + component]);

            ushort biomeData = (ushort)Math.Clamp(vertices[source + 17], 0.0f, ushort.MaxValue);
            packed[destination + BiomeDataOffset] = (byte)biomeData;
            packed[destination + BiomeDataOffset + 1] = (byte)(biomeData >> 8);
        }

        return packed;
    }

    private static byte ToSnormByte(float value)
    {
        value = Math.Clamp(value, -1.0f, 1.0f);
        return unchecked((byte)(sbyte)MathF.Round(value * 127.0f));
    }

    private static void WriteHalf(byte[] destination, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((System.Half)value);
        destination[offset] = (byte)bits;
        destination[offset + 1] = (byte)(bits >> 8);
    }
}