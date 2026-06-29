using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public abstract partial class DecorationGenerator : Resource
{
    [Export] public Mesh[] DecorationMeshes { get; set; }

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

    protected readonly Dictionary<Vector2I, List<MultiMeshInstance3D>> _instances = [];
    protected readonly object _lock = new();

    protected abstract bool IsValidBiome(BiomeType biome);
    protected abstract string InstanceName { get; }

    protected virtual bool ShouldGenerateForChunk(Vector2I coord) => true;
    protected virtual void OnPositionsSampled(Vector2I coord, ref List<(Vector3, int)> positions) { }

    private void ValidateDecorationMeshes()
    {
        if (DecorationMeshes == null || DecorationMeshes.Length == 0)
        {
            GD.PushWarning($"{InstanceName} has no decoration meshes assigned.");
            return;
        }

        for (var i = 0; i < DecorationMeshes.Length; i++)
        {
            if (!IsInstanceValid(DecorationMeshes[i]))
            {
                GD.PushWarning($"{InstanceName} {nameof(DecorationMeshes)}[{i}] is not assigned.");
            }
        }
    }

    public virtual void Initialize(TerrainController terrain)
    {
        ClearAll();
        _terrain = terrain;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = PlacementFrequency
        };
        ValidateDecorationMeshes();
    }

    public virtual void GenerateForChunk(Vector2I coord)
    {
        if (DecorationMeshes == null || DecorationMeshes.Length == 0)
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
            _terrain.AddChild(instance);
    }

    public virtual void RemoveForChunk(Vector2I coord)
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

    private List<(Vector3 position, int meshIndex)> SamplePositions(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;
        var step = chunkSize / (float)resolution;

        var results = new List<(Vector3, int)>();
        var random = new Random(coord.X * 10000 + coord.Y * RandomSeedBase);

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
                {
                    var meshIndex = random.Next(DecorationMeshes.Length);
                    results.Add((new Vector3(worldX, height, worldZ), meshIndex));
                }
            }
        }

        return results;
    }

    private List<MultiMeshInstance3D> CreateInstances(Vector2I coord, List<(Vector3 position, int meshIndex)> positions)
    {
        var instances = new List<MultiMeshInstance3D>();
        var groups = new Dictionary<int, List<Vector3>>();

        foreach (var (pos, idx) in positions)
        {
            if (!groups.ContainsKey(idx))
                groups[idx] = [];
            groups[idx].Add(pos);
        }

        foreach (var (meshIdx, meshPositions) in groups)
        {
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                InstanceCount = meshPositions.Count,
                Mesh = DecorationMeshes[meshIdx]
            };
            
            var random = new Random(coord.X * 10000 + coord.Y + RandomSeedBase + meshIdx);
            for (int i = 0; i < meshPositions.Count; i++)
            {
                var rotation = (float)(random.NextDouble() * Math.PI * 2);
                var scale = (float)(random.NextDouble() * (MaxScale - MinScale) + MinScale);
                var pos = meshPositions[i];
                pos.Y += HeightOffset * scale;

                var transform = Transform3D.Identity
                    .Rotated(Vector3.Up, rotation)
                    .Scaled(new Vector3(scale, scale, scale))
                    .Translated(pos);

                multiMesh.SetInstanceTransform(i, transform);
            }

            var chunkPos = _terrain.GetChunkCoord(coord.X, coord.Y);
            var instance = new MultiMeshInstance3D
            {
                Name = $"{InstanceName}_{meshIdx}_{chunkPos.X}_{chunkPos.Y}",
                Multimesh = multiMesh,
                Position = new Vector3(chunkPos.X, 0.0f, chunkPos.Y)
            };
            instances.Add(instance);
        }

        return instances;
    }

    private int GetBiomeDensity(BiomeType biome)
    {
        return biome == BiomeType.Forest ? ForestDensity : GrasslandDensity;
    }
}
