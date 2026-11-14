using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

public static class GameWorld
{
    private const float Gravity = -40.0f;
    private const float EyeHeight = 1.6f;   // camera -> feet
    private const float HeadMargin = 0.2f;   // camera -> top of head
    private const float Radius = 0.3f;   // half-width of the player
    private const float Skin = 0.01f;  // gap kept between player and surfaces
    private const float GroundSkin = 0.05f;  // tolerance for the ground check
    private static readonly Vector3 VoxelOffset = new(0.5f);

    private static readonly float[] BodyHeights = { -EyeHeight + 0.1f, -EyeHeight * 0.5f, HeadMargin - 0.1f };

    // Footprint sample points (relative to camera, XZ): center + 4 corners.
    private static readonly Vector2[] Footprint =
    {
        new(0, 0),
        new(-Radius, -Radius), new(Radius, -Radius),
        new(-Radius,  Radius), new(Radius,  Radius),
    };

    private static KeyboardState KeyboardState => Game._keyboardState;
    private static Camera Camera => Game._camera;

    private static Vector3 Velocity = Vector3.Zero;

    private static bool Cast(Vector3 origin, Vector3 dir, float reach,
                         out float distance, out Vector3 position)
    {
        bool ok = VoxelRaycaster.Raycast(origin + VoxelOffset, dir, reach, true, out var hit);
        distance = ok ? hit.Distance : 0f;
        position = ok ? hit.Position - VoxelOffset : default; // back to render space
        return ok;
    }

    // ---------- Collision helpers ----------

    /// <summary>Distance you can move along a horizontal axis before hitting a wall.</summary>
    private static float AllowedHorizontal(Vector3 axis, float amount)
    {
        float reach = Radius + MathF.Abs(amount);
        float allowed = MathF.Abs(amount);

        // Perpendicular direction used to spread rays across the player's width.
        Vector3 side = axis.X != 0 ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 dir = axis * MathF.Sign(amount);

        foreach (float h in BodyHeights)
        {
            for (int i = -1; i <= 1; i++)
            {
                Vector3 origin = Camera.Position + new Vector3(0, h, 0) + side * (i * Radius);

                if (Cast(origin, dir, reach, out float dist, out _))
                {
                    float free = MathF.Max(0f, dist - Radius - Skin);
                    allowed = MathF.Min(allowed, free);
                }
            }
        }

        return allowed * MathF.Sign(amount);
    }

    /// <summary>Casts down/up from the footprint points; returns the closest surface Y.</summary>
    private static bool CastVertical(bool down, float reach, out float surfaceY)
    {
        surfaceY = down ? float.NegativeInfinity : float.PositiveInfinity;
        bool any = false;
        Vector3 dir = down ? -Vector3.UnitY : Vector3.UnitY;

        foreach (var p in Footprint)
        {
            Vector3 origin = Camera.Position + new Vector3(p.X, 0, p.Y);

            if (Cast(origin, dir, reach, out _, out Vector3 hitPos))
            {
                any = true;
                surfaceY = down ? MathF.Max(surfaceY, hitPos.Y)
                                : MathF.Min(surfaceY, hitPos.Y);
            }
        }

        return any;
    }

    private static bool Grounded => CastVertical(true, EyeHeight + GroundSkin, out _);

    // ---------- Update ----------

    public static void Heartbeat(float dt)
    {
        dt = MathF.Min(dt, 1f / 20f); // avoid tunnelling on frame spikes

        Vector3 forward = new Vector3(Camera.Front.X, 0, Camera.Front.Z);
        if (forward.LengthSquared > 0)
            forward = Vector3.Normalize(forward);

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        Vector3 move = Vector3.Zero;
        if (KeyboardState.IsKeyDown(Keys.W)) move += forward;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= forward;
        if (KeyboardState.IsKeyDown(Keys.D)) move += right;
        if (KeyboardState.IsKeyDown(Keys.A)) move -= right;

        if (move.LengthSquared > 0)
            move = Vector3.Normalize(move) * Camera.Speed;

        Velocity = new Vector3(move.X, Velocity.Y, move.Z);

        bool grounded = Grounded;

        if (grounded && Velocity.Y < 0)
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);

        if (grounded && KeyboardState.IsKeyDown(Keys.Space))
            Velocity = new Vector3(Velocity.X, 10.0f, Velocity.Z);

        if (!grounded || Velocity.Y > 0)
            Velocity += new Vector3(0, Gravity, 0) * dt;

        Vector3 displacement = Velocity * dt;

        // --- X axis ---
        if (displacement.X != 0)
            Camera.Position += new Vector3(AllowedHorizontal(Vector3.UnitX, displacement.X), 0, 0);

        // --- Z axis ---
        if (displacement.Z != 0)
            Camera.Position += new Vector3(0, 0, AllowedHorizontal(Vector3.UnitZ, displacement.Z));

        // --- Y axis ---
        if (displacement.Y != 0)
        {
            bool down = displacement.Y < 0;
            float reach = (down ? EyeHeight : HeadMargin) + MathF.Abs(displacement.Y);

            if (CastVertical(down, reach, out float surfaceY))
            {
                float y = down ? surfaceY + EyeHeight : surfaceY - HeadMargin;
                Camera.Position = new Vector3(Camera.Position.X, y, Camera.Position.Z);
                Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
            }
            else
            {
                Camera.Position += new Vector3(0, displacement.Y, 0);
            }
        }
    }
}