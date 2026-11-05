using OpenTK.Mathematics;

public class Frustum
{
    private readonly Vector4[] _planes = new Vector4[6];

    // pass view * projection (OpenTK row-vector order)
    public void Update(Matrix4 viewProj)
    {
        Vector4 c0 = viewProj.Column0;
        Vector4 c1 = viewProj.Column1;
        Vector4 c2 = viewProj.Column2;
        Vector4 c3 = viewProj.Column3;

        _planes[0] = c3 + c0; // left
        _planes[1] = c3 - c0; // right
        _planes[2] = c3 + c1; // bottom
        _planes[3] = c3 - c1; // top
        _planes[4] = c3 + c2; // near
        _planes[5] = c3 - c2; // far

        for (int i = 0; i < 6; i++)
        {
            float len = new Vector3(_planes[i].X, _planes[i].Y, _planes[i].Z).Length;
            _planes[i] /= len;
        }
    }

    public bool IsBoxVisible(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector4 pl = _planes[i];

            Vector3 p = new Vector3(
                pl.X >= 0 ? max.X : min.X,
                pl.Y >= 0 ? max.Y : min.Y,
                pl.Z >= 0 ? max.Z : min.Z);

            if (pl.X * p.X + pl.Y * p.Y + pl.Z * p.Z + pl.W < 0)
                return false; // fully outside this plane
        }
        return true;
    }
}