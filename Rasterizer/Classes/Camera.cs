using OpenTK.Mathematics;

public class Camera
{
    public Vector3 Position;
    public float Yaw;    // degrees
    public float Pitch;  // degrees

    private float _targetYaw;
    private float _targetPitch;

    public float Speed = 8f;
    public float Sensitivity = 0.15f;
    public float RotationSmoothing = 24f;

    public Camera(Vector3 position, Vector3 target)
    {
        Position = position;

        Vector3 dir = Vector3.Normalize(target - position);
        Yaw = MathHelper.RadiansToDegrees(MathF.Atan2(dir.Z, dir.X));
        Pitch = MathHelper.RadiansToDegrees(MathF.Asin(dir.Y));
        _targetYaw = Yaw;
        _targetPitch = Pitch;
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

    public void Look(Vector2 mouseDelta, float deltaTime)
    {
        _targetYaw += mouseDelta.X * Sensitivity;
        _targetPitch -= mouseDelta.Y * Sensitivity;
        _targetPitch = Math.Clamp(_targetPitch, -89f, 89f);

        // Frame-rate independent exponential smoothing. The mouse can still
        // move freely, but individual raw-delta spikes no longer snap the view.
        float blend = 1f - MathF.Exp(-RotationSmoothing * MathF.Max(deltaTime, 0f));
        Yaw = MathHelper.Lerp(Yaw, _targetYaw, blend);
        Pitch = MathHelper.Lerp(Pitch, _targetPitch, blend);
    }
}