using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class TreePlacer : Resource
{
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    public int ForestDensity { get; set; } = 10; 
    [Export(PropertyHint.Range, "0, 50, 1, prefer_slider")]
    public int JungleDensity { get; set; } = 20;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float MinHeightThreshold { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float MaxHeightThreshold { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float MinScale { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0.5f, 3.0f, 0.1f, prefer_slider")]
    public float MaxScale { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "1, 200, 1, prefer_slider")]
    public int DensityNoiseScale { get; set; } = 50;
    [Export(PropertyHint.Range, "0, 20, 1, prefer_slider")]
    public int DensityNoiseAmplitude { get; set; } = 5;
    [Export(PropertyHint.Range, "0, 100000, 1, prefer_slider")]
    public int RandomSeedBase { get; set; } = 54321;
    [Export(PropertyHint.Range, "0.01f, 0.5f, 0.001f, prefer_slider")]
    public float PlacementFrequency { get; set; } = 0.03f;

    private TerrainController _terrain;
    private FastNoiseLite _placementNoise;

    private readonly Dictionary<Vector2I, MultiMeshInstance3D> _instances = [];
    private readonly object _lock = new();

    public void Initialize(TerrainController terrain)
    {
        _terrain = terrain;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = PlacementFrequency
        };
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (!IsInstanceValid(_terrain.TreeTemplate))
            return;

        var playerChunk = _terrain.CurrentChunkCoord;
        if (Math.Abs(coord.X - playerChunk.X) > _terrain.RenderDistance || 
            Math.Abs(coord.Y - playerChunk.Y) > _terrain.RenderDistance)
            return;

        var chunkSize = _terrain.ChunkSize;

        var centerHeight = _terrain.GetHeight(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);
        var normalizedCenterHeight = (centerHeight / _terrain.Height + 1.0f) / 2.0f;

        var moisture = _terrain.GetMoisture(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);
        var temperature = _terrain.GetTemperature(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);

        var biome = _terrain.GetBiome(moisture, temperature, normalizedCenterHeight);

        if (biome != BiomeType.Forest && biome != BiomeType.Jungle)
            return;

        if (normalizedCenterHeight < MinHeightThreshold || normalizedCenterHeight > MaxHeightThreshold)
            return;

        var density = biome == BiomeType.Jungle ? JungleDensity : ForestDensity;
        var adjustedDensity = density + (int)(_placementNoise.GetNoise2D(coord.X * DensityNoiseScale, coord.Y * DensityNoiseScale) * DensityNoiseAmplitude);
        adjustedDensity = Math.Max(0, adjustedDensity);

        var positions = SamplePositions(coord, adjustedDensity);
        if (positions.Count == 0)
            return;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count,
            Mesh = _terrain.TreeTemplate.Mesh
        };

        var random = new Random(coord.X * 10000 + coord.Y + RandomSeedBase);
        for (int i = 0; i < positions.Count; i++)
        {
            var rotation = (float)(random.NextDouble() * Math.PI * 2);
            var scale = (float)(random.NextDouble() * (MaxScale - MinScale) + MinScale);
            var pos = positions[i];

            var transform = Transform3D.Identity
                .Rotated(Vector3.Up, rotation)
                .Scaled(new Vector3(scale, scale, scale))
                .Translated(pos);

            multiMesh.SetInstanceTransform(i, transform);
        }

        var chunkPos = _terrain.GetChunkCoord(coord.X, coord.Y);
        var newInstance = new MultiMeshInstance3D
        {
            Name = $"Trees_{chunkPos.X}_{chunkPos.Y}",
            Multimesh = multiMesh,
            Position = new Vector3(chunkPos.X, 0, chunkPos.Y)
        };

        lock (_lock)
        {
            if (_instances.TryGetValue(coord, out var instance))
            {
                instance.QueueFree();
                _instances.Remove(coord);
            }
            _instances[coord] = newInstance;
        }

        _terrain.AddChild(newInstance);
    }

    public void RemoveForChunk(Vector2I coord)
    {
        lock (_lock)
        {
            if (_instances.TryGetValue(coord, out var instance))
            {
                instance.QueueFree();
                _instances.Remove(coord);
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var instance in _instances.Values)
            {
                instance.QueueFree();
            }
            _instances.Clear();
        }
    }

    public void RegenerateAll()
    {
        List<Vector2I> coords;
        lock (_lock)
        {
            coords = [.. _instances.Keys];
        }

        foreach (var coord in coords)
        {
            GenerateForChunk(coord);
        }
    }

    private List<Vector3> SamplePositions(Vector2I coord, int count)
    {
        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;

        var step = chunkSize / (float)resolution;
        var candidates = new List<Vector3>();

        for (float x = step; x < chunkSize; x += step)
        {
            for (float z = step; z < chunkSize; z += step)
            {
                var worldX = offsetX + x;
                var worldZ = offsetZ + z;

                var height = _terrain.GetHeight(worldX, worldZ);
                var normalizedHeight = (height / _terrain.Height + 1.0f) / 2.0f;

                if (normalizedHeight < MinHeightThreshold || normalizedHeight > MaxHeightThreshold)
                    continue;

                var moisture = _terrain.GetMoisture(worldX, worldZ);
                var temperature = _terrain.GetTemperature(worldX, worldZ);

                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);
                if (biome != BiomeType.Forest && biome != BiomeType.Jungle)
                    continue;

                var noiseVal = _placementNoise.GetNoise2D(worldX, worldZ);
                if (noiseVal > 0.1f)
                {
                    candidates.Add(new Vector3(worldX, height, worldZ));
                }
            }
        }

        if (candidates.Count <= count)
            return candidates;

        var random = new Random();
        var sampled = new HashSet<int>();

        while (sampled.Count < count)
        {
            var index = random.Next(candidates.Count);
            sampled.Add(index);
        }

        var result = new List<Vector3>();
        foreach (var index in sampled)
        {
            result.Add(candidates[index]);
        }

        return result;
    }
}
