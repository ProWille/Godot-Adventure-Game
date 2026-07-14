using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class BladeGrassGenerator : Resource
{
    [Export] public QuadMesh BladeMesh { get; set; }
    [Export] public ShaderMaterial GrassMaterial { get; set; }
    [Export(PropertyHint.Range, "0, 20, 1, prefer_slider")]
    public int BladesPerSample { get; set; } = 4;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float MinScale { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float MaxScale { get; set; } = 1.3f;

    private TerrainController _terrain;
    private readonly Dictionary<Vector2I, List<MultiMeshInstance3D>> _instances = [];
    private readonly object _lock = new();

    public void Initialize(TerrainController terrain)
    {
        ClearAll();
        _terrain = terrain;

        if (!IsInstanceValid(BladeMesh))
            GD.PushWarning($"{nameof(BladeMesh)} is not assigned.");
        if (!IsInstanceValid(GrassMaterial))
            GD.PushWarning($"{nameof(GrassMaterial)} is not assigned.");
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (!IsInstanceValid(BladeMesh) || !IsInstanceValid(GrassMaterial))
            return;

        if (!ShouldGenerateForChunk(coord))
            return;

        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;
        var step = chunkSize / (float)resolution;

        var positions = new List<Vector3>();
        var random = new Random(coord.X * 10000 + coord.Y * 54321);

        for (float x = step * 0.5f; x < chunkSize; x += step)
        {
            for (float z = step * 0.5f; z < chunkSize; z += step)
            {
                var worldX = offsetX + x;
                var worldZ = offsetZ + z;

                var height = _terrain.GetHeightmap(worldX, worldZ);
                if (height < _terrain.GetWaterLevel())
                    continue;

                var normalizedHeight = _terrain.GetNormalizedHeight(worldX, worldZ);
                var moisture = _terrain.GetMoisture(worldX, worldZ);
                var temperature = _terrain.GetTemperature(worldX, worldZ);
                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);

                if (biome != BiomeType.Grassland && biome != BiomeType.Forest)
                    continue;

                for (int b = 0; b < BladesPerSample; b++)
                {
                    var jitterX = (float)(random.NextDouble() - 0.5) * step * 0.5f;
                    var jitterZ = (float)(random.NextDouble() - 0.5) * step * 0.5f;
                    var jitteredHeight = _terrain.GetHeightmap(worldX + jitterX, worldZ + jitterZ);
                    positions.Add(new Vector3(worldX + jitterX, jitteredHeight, worldZ + jitterZ));
                }
            }
        }

        if (positions.Count == 0)
            return;

        var instances = CreateInstances(coord, positions);

        lock (_lock)
        {
            if (_instances.TryGetValue(coord, out var oldInstances))
            {
                foreach (var old in oldInstances)
                    old.QueueFree();
                _instances.Remove(coord);
            }
            _instances[coord] = instances;
        }

        foreach (var instance in instances)
            _terrain.ChunkContainer.AddChild(instance);
    }

    public void RemoveForChunk(Vector2I coord)
    {
        lock (_lock)
        {
            if (_instances.TryGetValue(coord, out var instances))
            {
                foreach (var instance in instances)
                {
                    if (IsInstanceValid(instance))
                        instance.QueueFree();
                }
                _instances.Remove(coord);
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var instances in _instances.Values)
            {
                foreach (var instance in instances)
                {
                    if (IsInstanceValid(instance))
                        instance.QueueFree();
                }
            }
            _instances.Clear();
        }
    }

    private List<MultiMeshInstance3D> CreateInstances(Vector2I coord, List<Vector3> positions)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count,
            Mesh = BladeMesh
        };

        var random = new Random(coord.X * 10000 + coord.Y + 98765);
        var chunkWorldPos = _terrain.GetWorldCoord(coord);

        for (int i = 0; i < positions.Count; i++)
        {
            var rot = (float)(random.NextDouble() * Mathf.Pi * 2.0);
            var scale = (float)(random.NextDouble() * (MaxScale - MinScale) + MinScale);
            var pos = positions[i];

            var localPos = pos - chunkWorldPos;
            var transform = Transform3D.Identity
                .Rotated(Vector3.Up, rot)
                .Scaled(new Vector3(scale, scale, scale))
                .Translated(localPos);

            multiMesh.SetInstanceTransform(i, transform);
        }

        var instance = new MultiMeshInstance3D
        {
            Name = $"BladeGrass_{coord.X}_{coord.Y}",
            Multimesh = multiMesh,
            MaterialOverride = GrassMaterial,
            Position = chunkWorldPos
        };

        return [instance];
    }

    private bool ShouldGenerateForChunk(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var step = chunkSize / (float)resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;

        for (float x = step * 0.5f; x < chunkSize; x += step * 2)
            for (float z = step * 0.5f; z < chunkSize; z += step * 2)
            {
                var normalizedHeight = _terrain.GetNormalizedHeight(offsetX + x, offsetZ + z);
                if (normalizedHeight < _terrain.OceanHeightThreshold)
                    continue;

                var moisture = _terrain.GetMoisture(offsetX + x, offsetZ + z);
                var temperature = _terrain.GetTemperature(offsetX + x, offsetZ + z);
                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);

                if (biome == BiomeType.Grassland || biome == BiomeType.Forest)
                    return true;
            }

        return false;
    }
}
