using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public abstract partial class DecorationGenerator : Resource
{
    [Export] public Mesh DecorationMesh { get; set; }

    [ExportGroup("Biome Density")]

    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int ForestDensity { get; set; } = 10;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrasslandDensity { get; set; } = 100;

    [ExportGroup("Density Noise")]

    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float DensityThreshold { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "1, 200, 1, prefer_slider")]
    public int DensityNoiseScale { get; set; } = 50;
    [Export(PropertyHint.Range, "0, 20, 1, prefer_slider")]
    public int DensityNoiseAmplitude { get; set; } = 5;
    [Export(PropertyHint.Range, "0.01f, 0.5f, 0.001f, prefer_slider")]
    public float PlacementFrequency { get; set; } = 0.03f;
    [Export(PropertyHint.Range, "0, 100000, 1, prefer_slider")]
    public int RandomSeedBase { get; set; } = 54321;

    [ExportGroup("Decoration Properties")]

    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float MinHeightThreshold { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float MaxHeightThreshold { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "0.5f, 10.0f, 0.1f, prefer_slider")]
    public float MinScale { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0.5f, 10.0f, 0.1f, prefer_slider")]
    public float MaxScale { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "-5.0f, 5.0f, 0.1f, prefer_slider")]
    public float HeightOffset { get; set; } = 0.0f;

    protected TerrainController _terrain;
    protected FastNoiseLite _placementNoise;

    protected readonly Dictionary<Vector2I, MultiMeshInstance3D> _instances = [];
    protected readonly object _lock = new();

    protected abstract bool IsValidBiome(BiomeType biome);
    protected abstract string InstanceName { get; }

    protected virtual bool ShouldGenerateForChunk(Vector2I coord) => true;
    protected virtual void OnPositionsSampled(Vector2I coord, ref List<Vector3> positions) { }

    public virtual void Initialize(TerrainController terrain)
    {
        ClearAll();
        _terrain = terrain;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = PlacementFrequency
        };

        if (!IsInstanceValid(DecorationMesh))
        {
            GD.PushWarning($"{InstanceName} {nameof(DecorationMesh)} is not assigned.");
        }
    }

    public virtual void GenerateForChunk(Vector2I coord)
    {
        if (!IsInstanceValid(DecorationMesh))
            return;

        var playerChunk = _terrain.CurrentChunkCoord;
        if (Math.Abs(coord.X - playerChunk.X) > _terrain.RenderDistance || 
            Math.Abs(coord.Y - playerChunk.Y) > _terrain.RenderDistance)
            return;

        if (!ShouldGenerateForChunk(coord))
            return;

        var positions = SamplePositions(coord);
        OnPositionsSampled(coord, ref positions);
        if (positions.Count == 0)
            return;

        var newInstance = CreateInstance(coord, positions);

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

    public virtual void RemoveForChunk(Vector2I coord)
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

    public virtual void RegenerateAll()
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

    public virtual void ClearAll()
    {
        lock (_lock)
        {
            foreach (var instance in _instances.Values)
            {
                if (IsInstanceValid(instance))
                    instance.QueueFree();
            }
            _instances.Clear();
        }
    }

    private List<Vector3> SamplePositions(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;

        var step = chunkSize / (float)resolution;
        var positions = new List<Vector3>();

        for (float x = step; x < chunkSize; x += step)
        {
            for (float z = step; z < chunkSize; z += step)
            {
                var worldX = offsetX + x;
                var worldZ = offsetZ + z;

                var normalizedHeight = _terrain.GetNormalizedHeight(worldX, worldZ);
                if (normalizedHeight < MinHeightThreshold || normalizedHeight > MaxHeightThreshold)
                    continue;

                var moisture = _terrain.GetMoisture(worldX, worldZ);
                var temperature = _terrain.GetTemperature(worldX, worldZ);

                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);
                if (!IsValidBiome(biome))
                    continue;

                var noiseValue = _placementNoise.GetNoise2D(worldX, worldZ);
                var normalizedNoise = (noiseValue + 1.0f) * 0.5f;
                var density = GetBiomeDensity(biome);
                var threshold = 1.0f - DensityThreshold * Math.Min(density * 0.02f, 1.0f);

                var height = _terrain.GetBlendedHeightmap(worldX, worldZ);
                if (height < _terrain.GetWaterLevel())
                    continue;

                if (normalizedNoise > threshold)
                    positions.Add(new Vector3(worldX, height, worldZ));
            }
        }

        return positions;
    }

    private MultiMeshInstance3D CreateInstance(Vector2I coord, List<Vector3> positions)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count,
            Mesh = DecorationMesh
        };

        var random = new Random(coord.X * 10000 + coord.Y + RandomSeedBase);
        for (int i = 0; i < positions.Count; i++)
        {
            var rotation = (float)(random.NextDouble() * Math.PI * 2);
            var scale = (float)(random.NextDouble() * (MaxScale - MinScale) + MinScale);
            var pos = positions[i];
            pos.Y += HeightOffset * scale;

            var transform = Transform3D.Identity
                .Rotated(Vector3.Up, rotation)
                .Scaled(new Vector3(scale, scale, scale))
                .Translated(pos);

            multiMesh.SetInstanceTransform(i, transform);
        }

        var chunkPos = _terrain.GetChunkCoord(coord.X, coord.Y);
        var newInstance = new MultiMeshInstance3D
        {
            Name = $"{InstanceName}_{chunkPos.X}_{chunkPos.Y}",
            Multimesh = multiMesh,
            Position = new Vector3(chunkPos.X, 0.0f, chunkPos.Y)
        };

        return newInstance;
    }

    private int GetBiomeDensity(BiomeType biome)
    {
        return biome == BiomeType.Forest ? ForestDensity : GrasslandDensity;
    }
}
