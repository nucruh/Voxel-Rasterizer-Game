
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

public static class GameWorld
{
    private const float Gravity = -40f;
    private const float EyeHeight = 1.6f;
    private const float HeadMargin = 0.2f;
    private const float Radius = 0.3f;
    private const float Skin = 0.01f;
    private const float GroundSkin = 0.05f;

    private static readonly Vector3 VoxelOffset = new(0.5f);

    private static readonly float[] BodyHeights =
    {
        -EyeHeight + 0.1f,
        -EyeHeight * 0.5f,
        HeadMargin - 0.1f
    };

    private static readonly Vector2[] Footprint =
    {
        new(0, 0),
        new(-Radius, -Radius), new(Radius, -Radius),
        new(-Radius, Radius), new(Radius, Radius)
    };

    private static KeyboardState KeyboardState => Game._keyboardState;
    private static Camera Camera => Game._camera;

    private static Vector3 Velocity;
    private static bool _flying;

    public static AudioManager Audio = new();

    private static bool Cast(Vector3 origin, Vector3 direction, float reach, out float distance, out Vector3 position)
    {
        bool hit = VoxelRaycaster.Raycast(origin + VoxelOffset, direction, reach, true, out var result);

        distance = hit ? result.Distance : 0f;
        position = hit ? result.Position - VoxelOffset : default;

        return hit;
    }

    private static float AllowedHorizontal(Vector3 axis, float amount)
    {
        float allowed = MathF.Abs(amount);
        Vector3 side = axis.X != 0 ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 direction = axis * MathF.Sign(amount);

        foreach (float height in BodyHeights)
        {
            for (int i = -1; i <= 1; i++)
            {
                Vector3 origin = Camera.Position + new Vector3(0, height, 0) + side * (i * Radius);

                if (Cast(origin, direction, Radius + allowed, out float distance, out _))
                    allowed = MathF.Min(allowed, MathF.Max(0, distance - Radius - Skin));
            }
        }

        return allowed * MathF.Sign(amount);
    }

    private static bool CastVertical(bool down, float reach, out float surfaceY)
    {
        surfaceY = down ? float.NegativeInfinity : float.PositiveInfinity;
        bool hit = false;
        Vector3 direction = down ? -Vector3.UnitY : Vector3.UnitY;

        foreach (Vector2 point in Footprint)
        {
            Vector3 origin = Camera.Position + new Vector3(point.X, 0, point.Y);

            if (!Cast(origin, direction, reach, out _, out Vector3 position))
                continue;

            surfaceY = down ? MathF.Max(surfaceY, position.Y) : MathF.Min(surfaceY, position.Y);
            hit = true;
        }

        return hit;
    }

    private static bool Grounded => CastVertical(true, EyeHeight + GroundSkin, out _);

    public static void Load()
    {
        Audio.Load("wind01", "Sounds/Ambient/wind01.wav");
        Audio.Load("break", "Sounds/Effects/break.wav");
        Audio.Play("wind01", 0.14f, loop: true);
    }

    private static void Movement(float dt)
    {
        if (KeyboardState.IsKeyPressed(Keys.E))
        {
            _flying = !_flying;
            Velocity = Vector3.Zero;
        }

        Vector3 forward = new(Camera.Front.X, 0, Camera.Front.Z);

        if (forward.LengthSquared > 0)
            forward.Normalize();

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        Vector3 move = Vector3.Zero;

        if (KeyboardState.IsKeyDown(Keys.W)) move += forward;
        if (KeyboardState.IsKeyDown(Keys.S)) move -= forward;
        if (KeyboardState.IsKeyDown(Keys.D)) move += right;
        if (KeyboardState.IsKeyDown(Keys.A)) move -= right;

        if (_flying)
        {
            if (KeyboardState.IsKeyDown(Keys.Space)) move.Y++;
            if (KeyboardState.IsKeyDown(Keys.LeftShift)) move.Y--;

            if (move.LengthSquared > 0)
                move = Vector3.Normalize(move) * Camera.Speed;

            Camera.Position += move * dt * 5f;
            return;
        }

        float sprint = KeyboardState.IsKeyDown(Keys.LeftShift) ? 1.5f : 1f;

        if (move.LengthSquared > 0)
            move = Vector3.Normalize(move) * Camera.Speed * sprint;

        Velocity = new Vector3(move.X, Velocity.Y, move.Z);

        bool grounded = Grounded;

        if (grounded && Velocity.Y < 0)
            Velocity.Y = 0;

        if (grounded && KeyboardState.IsKeyDown(Keys.Space))
            Velocity.Y = 10f;

        if (!grounded || Velocity.Y > 0)
            Velocity.Y += Gravity * dt;

        Vector3 displacement = Velocity * dt;

        if (displacement.X != 0)
            Camera.Position.X += AllowedHorizontal(Vector3.UnitX, displacement.X);

        if (displacement.Z != 0)
            Camera.Position.Z += AllowedHorizontal(Vector3.UnitZ, displacement.Z);

        if (displacement.Y == 0)
            return;

        bool down = displacement.Y < 0;
        float reach = (down ? EyeHeight : HeadMargin) + MathF.Abs(displacement.Y);

        if (CastVertical(down, reach, out float surfaceY))
        {
            Camera.Position.Y = down ? surfaceY + EyeHeight : surfaceY - HeadMargin;
            Velocity.Y = 0;
        }
        else
        {
            Camera.Position.Y += displacement.Y;
        }
    }

    private static void LeftClick()
    {
        Vector3 origin = Camera.Position + new Vector3(0, EyeHeight * 0.5f, 0);
        Vector3 direction = Camera.Front;
        if (VoxelRaycaster.Raycast(origin + VoxelOffset, direction, 5f, true, out var result))
        {
            Console.WriteLine($"Left-clicked on voxel at {result.BlockPosition}");

            Chunk? chunk = Game._chunks.GetValueOrDefault(Vector3i.Divide(result.BlockPosition, Chunk.Size));
            
            if (chunk != null)
            {
                chunk.SetBlock(
                    result.BlockPosition.X % Chunk.Size,
                    result.BlockPosition.Y % Chunk.Size,
                    result.BlockPosition.Z % Chunk.Size,
                    0);

                //chunk.CreateMesh();
            }

            Audio.Play("break", 0.5f);
        }
    }

    public static void Heartbeat(float dt)
    {
        if (Game._keyboardState is null)
            return;

        dt = MathF.Min(dt, 1f / 20f);

        Movement(dt);

        if (Game._mouseState is not null && Game._mouseState.IsButtonPressed(MouseButton.Left))
        {
            LeftClick();
        }
    }

}
