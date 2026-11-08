using System.Text.Json;
using OpenTK.Mathematics;

public class Model
{
    public string Name = "";
    public ushort BlockId;
    public bool IsTransparent;
    public bool Collision;

    public string DiffuseTexture = "";
    public string NormalTexture = "";

    public float[] Vertices = Array.Empty<float>();   // 17 floats per vertex, scale/origin already baked in
    public Mesh mesh = null!;
}

// json data loaded from model.json
public class ModelData
{
    public string Name { get; set; } = "";
    public ushort BlockId { get; set; }
    public bool IsTransparent { get; set; }
    public float[] Scale { get; set; } = { 1f, 1f, 1f };
    public float[] Origin { get; set; } = { 0f, 0f, 0f };
    public bool Collision { get; set; }
    public TextureData Textures { get; set; } = new();
}

public class TextureData
{
    public string Diffuse { get; set; } = "";
    public string Normal { get; set; } = "";
}

public static class Models
{
    public static Dictionary<ushort, Model> Loaded = new();
    public static Dictionary<string, Model> ByName = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Initialize()
    {
        string modelsPath = Path.Combine(AppContext.BaseDirectory, "Models");

        foreach (string directory in Directory.GetDirectories(modelsPath))
        {
            string jsonPath = Path.Combine(directory, "model.json");
            string objPath = Path.Combine(directory, "mesh.obj");

            if (!File.Exists(jsonPath) || !File.Exists(objPath))
                continue;

            ModelData data = JsonSerializer.Deserialize<ModelData>(File.ReadAllText(jsonPath), JsonOptions)
                ?? throw new Exception($"Invalid model JSON: {directory}");

            Vector3 scale = ToVec3(data.Scale, Vector3.One);
            Vector3 origin = ToVec3(data.Origin, Vector3.Zero);

            float[] vertices = ObjLoader.Load(objPath, data.Textures.Diffuse, scale, origin);

            var model = new Model
            {
                Name = data.Name,
                BlockId = data.BlockId,
                IsTransparent = data.IsTransparent,
                Collision = data.Collision,
                DiffuseTexture = data.Textures.Diffuse,
                NormalTexture = data.Textures.Normal,
                Vertices = vertices,
                mesh = new Mesh(vertices)
            };

            Loaded.Add(data.BlockId, model);
            ByName.Add(data.Name, model);
        }
    }

    private static Vector3 ToVec3(float[]? a, Vector3 fallback) =>
        a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : fallback;

    public static Model Get(ushort blockId) => Loaded[blockId];
    public static Model Get(string name) => ByName[name];
}