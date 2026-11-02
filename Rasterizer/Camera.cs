using OpenTK.Mathematics;

public class Camera
{
    public Vector3 Position;
    public float Yaw;    // degrees
    public float Pitch;  // degrees

    public float Speed = 4f;
    public float Sensitivity = 0.15f;

    public Camera(Vector3 position, Vector3 target)
    {
        Position = position;

        Vector3 dir = Vector3.Normalize(target - position);
        Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Z, dir.X));
        Pitch = MathHelper.RadiansToDegrees(MathF.Asin(dir.Y));
    }

    public Vector3 Front
    {
        get
        {
            float yaw = MathHelper.DegreesToRadians(Yaw);
            float pitch = MathHelper.DegreesToRadians(Pitch);

            return Vector3.Normalize(new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw)));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));

    public Matrix4 GetViewMatrix() =>
        Matrix4.LookAt(Position, Position + Front, Vector3.UnitY);

    public void Look(Vector2 mouseDelta)
    {
        Yaw += mouseDelta.X * Sensitivity;
        Pitch -= mouseDelta.Y * Sensitivity;
        Pitch = Math.Clamp(Pitch, -89f, 89f);
    }
}