
using System;

public static class PerlinNoise
{
    private static readonly int[] Permutation = new int[512];

    static PerlinNoise()
    {
        int[] p = new int[256];

        for (int i = 0; i < 256; i++)
            p[i] = i;

        Random random = new Random(12345);

        for (int i = 255; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (int i = 0; i < 512; i++)
            Permutation[i] = p[i & 255];
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + t * (b - a);
    }

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;

        float u = h < 8 ? x : y;
        float v = h < 4 ? y : h == 12 || h == 14 ? x : z;

        return ((h & 1) == 0 ? u : -u) +
               ((h & 2) == 0 ? v : -v);
    }

    public static float Noise(float x, float y, float z)
    {
        int xi = (int)MathF.Floor(x) & 255;
        int yi = (int)MathF.Floor(y) & 255;
        int zi = (int)MathF.Floor(z) & 255;

        x -= MathF.Floor(x);
        y -= MathF.Floor(y);
        z -= MathF.Floor(z);

        float u = Fade(x);
        float v = Fade(y);
        float w = Fade(z);

        int a = Permutation[xi] + yi;
        int aa = Permutation[a] + zi;
        int ab = Permutation[a + 1] + zi;
        int b = Permutation[xi + 1] + yi;
        int ba = Permutation[b] + zi;
        int bb = Permutation[b + 1] + zi;

        return Lerp(
            Lerp(
                Lerp(Grad(Permutation[aa], x, y, z),
                     Grad(Permutation[ba], x - 1, y, z), u),
                Lerp(Grad(Permutation[ab], x, y - 1, z),
                     Grad(Permutation[bb], x - 1, y - 1, z), u), v),
            Lerp(
                Lerp(Grad(Permutation[aa + 1], x, y, z - 1),
                     Grad(Permutation[ba + 1], x - 1, y, z - 1), u),
                Lerp(Grad(Permutation[ab + 1], x, y - 1, z - 1),
                     Grad(Permutation[bb + 1], x - 1, y - 1, z - 1), u), v),
            w);
    }

    public static float Noise(float x, float y)
    {
        return Noise(x, y, 0f);
    }
}
