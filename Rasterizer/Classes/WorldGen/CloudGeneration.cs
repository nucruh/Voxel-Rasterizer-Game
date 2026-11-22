using System.Diagnostics;
using OpenTK.Mathematics;
using System.Collections.Concurrent;

public class CloudGeneration
{
    public static void GenerateClouds(List<float> vertices, Vector3 chunkPosition, int seed)
    {
        Random random = new(seed);
        int cloudCount = random.Next(5, 15);
        for (int i = 0; i < cloudCount; i++)
        {
            Vector3 cloudPosition = new(
                random.Next(-16, 16) + chunkPosition.X,
                random.Next(10, 20),
                random.Next(-16, 16) + chunkPosition.Z
            );
            float cloudSize = random.Next(4, 8);
            MeshUtility.AddCube(vertices, cloudPosition, new Vector3(cloudSize, cloudSize / 2, cloudSize));
        }
    }
}