using System;
using System.Collections.Generic;
using Godot;

internal class GrassPlacer
{
    private MeshInstance3D _template;

    private readonly int _grasslandDensity;
    private readonly int _forestDensity;
    private readonly int _jungleDensity;
    private readonly int _savannaDensity;

    private readonly float _minHeight;
    private readonly float _maxHeight;
    private readonly float _heightOffset;
    private readonly int _renderDistance;

    private readonly float _gridSpacing;
    private readonly float _densityThreshold;
    private readonly float _placementFrequency;
    private readonly float _variationFrequency;

    private readonly FastNoiseLite _placementNoise;
    private readonly FastNoiseLite _variationNoise;
    private readonly Dictionary<Vector2I, MultiMeshInstance3D> _instances = [];
    private readonly TerrainController _terrain;
    private readonly Node3D _parent;
    private readonly object _lock = new();

    public GrassPlacer(
        TerrainController terrain,
        Node3D parent,
        int grasslandDensity,
        int forestDensity,
        int jungleDensity,
        int savannaDensity,
        float minHeight,
        float maxHeight,
        float heightOffset,
        int renderDistance,
        float gridSpacing,
        float densityThreshold,
        float placementFrequency,
        float variationFrequency)
    {
        _terrain = terrain;
        _parent = parent;
        _grasslandDensity = grasslandDensity;
        _forestDensity = forestDensity;
        _jungleDensity = jungleDensity;
        _savannaDensity = savannaDensity;
        _minHeight = minHeight;
        _maxHeight = maxHeight;
        _heightOffset = heightOffset;
        _renderDistance = renderDistance;
        _gridSpacing = gridSpacing;
        _densityThreshold = densityThreshold;
        _placementFrequency = placementFrequency;
        _variationFrequency = variationFrequency;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = _placementFrequency
        };
        _variationNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = _variationFrequency
        };
    }

    public void SetTemplate(MeshInstance3D template)
    {
        _template = template;
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (_template == null)
            return;

        var playerChunk = _terrain.CurrentChunkCoord;
        if (Math.Abs(coord.X - playerChunk.X) > _renderDistance || 
            Math.Abs(coord.Y - playerChunk.Y) > _renderDistance)
            return;

        var (positions, rotationScales) = GetChunkVertexData(coord);
        if (positions.Count == 0 || rotationScales.Count == 0)
            return;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count
        };

        if (_template.Mesh != null)
        {
            multiMesh.Mesh = _template.Mesh;
        }

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
        var instance = new MultiMeshInstance3D
        {
            Name = $"Grass_{coord.X}_{coord.Y}",
            Multimesh = multiMesh,
            Position = new Vector3(chunkPos.X, 0, chunkPos.Y)
        };

        lock (_lock)
        {
            if (_instances.ContainsKey(coord))
            {
                _instances[coord].QueueFree();
                _instances.Remove(coord);
            }
            _instances[coord] = instance;
        }

        _parent.AddChild(instance);
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

        for (float x = 0; x <= chunkSize; x += _gridSpacing)
        {
            for (float z = 0; z <= chunkSize; z += _gridSpacing)
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
                    BiomeType.Jungle => _jungleDensity,
                    BiomeType.Forest => _forestDensity,
                    BiomeType.Savanna => _savannaDensity,
                    _ => _grasslandDensity
                };

                if (biomeDensity <= 0)
                    continue;

                var actualThreshold = (noiseValue + 1.0f) / 2.0f * _densityThreshold * (densityFactor / (float)biomeDensity);

                if (noiseValue < actualThreshold * 2.0f - 1.0f)
                    continue;

                positions.Add(new Vector3(worldX, height + _heightOffset, worldZ));

                var variation = (float)_variationNoise.GetNoise2D(worldX, worldZ);
                var rotation = (float)(variation * Math.PI * 2);
                var scaleMultiplier = (variation + 1) * 0.5f;
                var scaleVar = _minHeight + (_maxHeight - _minHeight) * scaleMultiplier;
                rotationScales.Add((rotation, scaleVar));
            }
        }

        return (positions, rotationScales);
    }

    private bool IsValidGrassBiome(BiomeType biome)
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
            BiomeType.Jungle => _jungleDensity,
            BiomeType.Forest => _forestDensity,
            BiomeType.Savanna => _savannaDensity,
            BiomeType.Grassland => _grasslandDensity,
            _ => 0
        };
    }
}
