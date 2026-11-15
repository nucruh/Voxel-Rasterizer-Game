#pragma warning disable CA1416 // System.Drawing is used for offline biome-map generation.

using System.Drawing;
using System.Drawing.Imaging;


public static class BiomeUtility
{
    // Scale is distances between extremes
    public static readonly int temperatureScale = 30000;
    public static readonly int humidityScale = 45000;


    public static readonly float humidityMin = 0.0f;
    public static readonly float humidityMax = 450.0f;
    public static readonly float temperatureMin = -10.0f;
    public static readonly float temperatureMax = 35.0f;

    public static readonly int temperatureNoiseScale = 8000;
    public static readonly float temperatureNoiseStrength = 80f;
    public static readonly float humidityNoiseStrength = 200f;

    public static readonly float warpScale = 60000f;       // feature size of the bends
    public static readonly float temperatureWarp = 20000f; // blocks; try ~temperatureScale or more
    public static readonly float humidityWarp = 20000f;

    // remap noise to [-1, 1] and stretch it, since FBM rarely hits the extremes
    static float Signed(float n01) => (n01 * 2f - 1f);   // use this if FBM returns [0,1]
                                                         // if FBM already returns [-1,1], skip the remap and just multiply by ~1.5-2 to compensate

    public static (float temperature, float humidity) GetBiomeData(float x, float z)
    {
        // Displacement fields (depend on BOTH x and z, which is what bends the stripes)
        float warpT = Signed(PerlinNoise.FBM(
            x / warpScale, z / warpScale, octaves: 1, seed: Game._worldSeed * 7));
        float warpH = Signed(PerlinNoise.FBM(
            x / warpScale, z / warpScale, octaves: 1, seed: Game._worldSeed * 13));

        // Warp the coordinate that the wave runs along
        float warpedX = x + warpT * temperatureWarp;
        float warpedZ = z + warpH * humidityWarp;

        float temperatureBase =
            (MathF.Cos(MathF.PI * warpedX / temperatureScale) * -0.5f + 0.5f) *
            (temperatureMax - temperatureMin) + temperatureMin;

        float humidityBase =
            (MathF.Cos(MathF.PI * warpedZ / humidityScale) * -0.5f + 0.5f) *
            (humidityMax - humidityMin) + humidityMin;

        float temperatureNoise = PerlinNoise.FBM(
            x / temperatureNoiseScale, z / temperatureNoiseScale,
            octaves: 8, seed: Game._worldSeed);
        float humidityNoise = PerlinNoise.FBM(
            z / temperatureNoiseScale, x / temperatureNoiseScale,
            octaves: 8, seed: Game._worldSeed + 1);

        return (
            temperatureBase + temperatureNoise * temperatureNoiseStrength,
            humidityBase + humidityNoise * humidityNoiseStrength
        );
    }

    private static Color GetHeatmapColor(float value)
    {
        value = Math.Clamp(value, 0, 1);

        if (value < 0.5f)
        {
            float t = value * 2;
            return Color.FromArgb(
                0,
                (int)(t * 255),
                (int)((1 - t) * 255)
            );
        }
        else
        {
            float t = (value - 0.5f) * 2;
            return Color.FromArgb(
                (int)(t * 255),
                (int)((1 - t) * 255),
                0
            );
        }
    }

    public static void GenerateBiomeAtlasMap(
    int width,
    int height,
    string atlasPath,
    string outputPath = "biomes.png",
    float worldOffsetX = 0,
    float worldOffsetZ = 0,
    float pixelScale = 1f)
    {
        using Bitmap atlas = new(atlasPath);
        using Bitmap biomeMap = new(width, height);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float worldX = worldOffsetX + x * pixelScale;
                float worldZ = worldOffsetZ + z * pixelScale;

                var (temperature, humidity) = GetBiomeData(worldX, worldZ);

                float normalizedTemperature =
                    (temperature - temperatureMin) / (temperatureMax - temperatureMin);

                float normalizedHumidity =
                    (humidity - humidityMin) / (humidityMax - humidityMin);

                Color biomeColor = GetAtlasColor(
                    atlas,
                    normalizedTemperature,
                    normalizedHumidity
                );

                biomeMap.SetPixel(x, z, biomeColor);
            }
        }

        biomeMap.Save(outputPath, ImageFormat.Png);
    }

    public static Color GetAtlasColor(
    Bitmap atlas,
    float normalizedTemperature,
    float normalizedHumidity)
    {
        int y = (int)(Math.Clamp(normalizedHumidity, 0, 1) * (atlas.Width - 1));
        int x = (int)(Math.Clamp(normalizedTemperature, 0, 1) * (atlas.Height - 1));

        return atlas.GetPixel(x, y);
    }

    public static (float temperature, float humidity) GetNormalizedBiomeData(float x, float z)
    {
        var (temperature, humidity) = GetBiomeData(x, z);

        return (
            Math.Clamp((temperature - temperatureMin) / (temperatureMax - temperatureMin), 0, 1),
            Math.Clamp((humidity - humidityMin) / (humidityMax - humidityMin), 0, 1)
        );
    }

    public static void GenerateBiomeMaps(
    int width,
    int height,
    float worldOffsetX = 0,
    float worldOffsetZ = 0,
    float pixelScale = 1f,
    string temperaturePath = "temperature.png",
    string humidityPath = "humidity.png")
    {

        using Bitmap temperatureMap = new(width, height);
        using Bitmap humidityMap = new(width, height);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float worldX = worldOffsetX + x * pixelScale;
                float worldZ = worldOffsetZ + z * pixelScale;

                var (temperature, humidity) = GetBiomeData(worldX, worldZ);

                float normalizedTemperature =
                    (temperature - temperatureMin) / (temperatureMax - temperatureMin);

                float normalizedHumidity =
                    (humidity - humidityMin) / (humidityMax - humidityMin);

                int t = (int)(Math.Clamp(normalizedTemperature, 0, 1) * 255);
                int h = (int)(Math.Clamp(normalizedHumidity, 0, 1) * 255);

                temperatureMap.SetPixel(x, z, GetHeatmapColor(normalizedTemperature));
                humidityMap.SetPixel(x, z, GetHeatmapColor(normalizedHumidity));
            }
        }

        temperatureMap.Save(temperaturePath, ImageFormat.Png);
        humidityMap.Save(humidityPath, ImageFormat.Png);
    }
}