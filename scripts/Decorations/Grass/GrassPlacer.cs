using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame;

public partial class GrassPlacer : Resource
{
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int GrasslandDensity { get; set; } = 100;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int ForestDensity { get; set; } = 80;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int JungleDensity { get; set; } = 120;
    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    public int SavannaDensity { get; set; } = 60;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    public float MinHeight { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    public float MaxHeight { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float HeightOffset { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0.2f, 5.0f, 0.1f, prefer_slider")]
    public float GridSpacing { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.05f, prefer_slider")]
    public float DensityThreshold { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0.01f, 2.0f, 0.01f, prefer_slider")]
    public float PlacementFrequency { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0.01f, 2.0f, 0.01f, prefer_slider")]
    public float VariationFrequency { get; set; } = 0.1f;

    private TerrainController _terrain;
    private FastNoiseLite _placementNoise;
    private FastNoiseLite _variationNoise;

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
        _variationNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = VariationFrequency
        };
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (!IsInstanceValid(_terrain.GrassTemplate))
            return;

        var playerChunk = _terrain.CurrentChunkCoord;
        if (Math.Abs(coord.X - playerChunk.X) > _terrain.RenderDistance || 
            Math.Abs(coord.Y - playerChunk.Y) > _terrain.RenderDistance)
            return;

        var (positions, rotationScales) = GetChunkVertexData(coord);
        if (positions.Count == 0 || rotationScales.Count == 0)
            return;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count,
            Mesh = _terrain.GrassTemplate.Mesh
        };

        for (int i = 0; i < positions.Count; i++)
        {
            var (rotation, scale) = rotationScales[i];
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
            Name = $"Grass_{coord.X}_{coord.Y}",
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

    private (List<Vector3> positions, List<(float, float)> rotationScales) GetChunkVertexData(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;

        var positions = new List<Vector3>();
        var rotationScales = new List<(float rotation, float scale)>();

        var biomeDensity = GetBiomeDensity(coord);

        for (float x = 0; x <= chunkSize; x += GridSpacing)
        {
            for (float z = 0; z <= chunkSize; z += GridSpacing)
            {
                var worldX = offsetX + x;
                var worldZ = offsetZ + z;

                var height = _terrain.HeightNoise.GetNoise2D(worldX, worldZ) * _terrain.Height;
                var normalizedHeight = (height / _terrain.Height + 1.0f) / 2.0f;
                var moisture = (_terrain.MoistureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
                var temperature = (_terrain.TemperatureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;

                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);
                if (!IsValidGrassBiome(biome))
                    continue;

                var noiseValue = _placementNoise.GetNoise2D(worldX, worldZ);
                var densityFactor = biome switch
                {
                    BiomeType.Jungle => JungleDensity,
                    BiomeType.Forest => ForestDensity,
                    BiomeType.Savanna => SavannaDensity,
                    _ => GrasslandDensity
                };

                if (biomeDensity <= 0)
                    continue;

                var actualThreshold = (noiseValue + 1.0f) / 2.0f * DensityThreshold * (densityFactor / (float)biomeDensity);

                if (noiseValue < actualThreshold * 2.0f - 1.0f)
                    continue;

                positions.Add(new Vector3(worldX, height + HeightOffset, worldZ));

                var variation = (float)_variationNoise.GetNoise2D(worldX, worldZ);
                var rotation = (float)(variation * Math.PI * 2);
                var scaleMultiplier = (variation + 1) * 0.5f;
                var scaleVar = MinHeight + (MaxHeight - MinHeight) * scaleMultiplier;
                rotationScales.Add((rotation, scaleVar));
            }
        }

        return (positions, rotationScales);
    }

    private static bool IsValidGrassBiome(BiomeType biome)
    {
        return biome == BiomeType.Grassland || biome == BiomeType.Forest ||
               biome == BiomeType.Jungle || biome == BiomeType.Savanna;
    }

    private int GetBiomeDensity(Vector2I coord)
    {
        var centerX = coord.X * _terrain.ChunkSize + _terrain.ChunkSize / 2f;
        var centerZ = coord.Y * _terrain.ChunkSize + _terrain.ChunkSize / 2f;

        var height = _terrain.HeightNoise.GetNoise2D(centerX, centerZ) * _terrain.Height;
        var normalizedHeight = (height / _terrain.Height + 1.0f) / 2.0f;
        var moisture = (_terrain.MoistureNoise.GetNoise2D(centerX, centerZ) + 1.0f) / 2.0f;
        var temperature = (_terrain.TemperatureNoise.GetNoise2D(centerX, centerZ) + 1.0f) / 2.0f;

        var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);

        return biome switch
        {
            BiomeType.Jungle => JungleDensity,
            BiomeType.Forest => ForestDensity,
            BiomeType.Savanna => SavannaDensity,
            BiomeType.Grassland => GrasslandDensity,
            _ => 0
        };
    }
}
