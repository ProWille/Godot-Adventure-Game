using System;
using System.Collections.Generic;
using Godot;

internal class GrassPlacer
{
    private MeshInstance3D _template;
    public MeshInstance3D Template => _template;

    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _grasslandDensity = 100;

    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _forestDensity = 80;

    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _jungleDensity = 120;

    [Export(PropertyHint.Range, "0, 500, 1, prefer_slider")]
    private int _savannaDensity = 60;

    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    private readonly float _minHeight = 0.5f;

    [Export(PropertyHint.Range, "0.1f, 2.0f, 0.1f, prefer_slider")]
    private readonly float _maxHeight = 1.5f;

    [Export] private float _heightThreshold = 0.3f;
    public float HeightThreshold => _heightThreshold;

    private readonly FastNoiseLite _placementNoise;
    private readonly Dictionary<Vector2I, MultiMeshInstance3D> _instances = [];
    private readonly TerrainController _terrain;
    private readonly Node3D _parent;
    private readonly object _lock = new();

    public GrassPlacer(TerrainController terrain, Node3D parent)
    {
        _terrain = terrain;
        _parent = parent;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = 0.05f
        };
    }

    public void SetTemplate(MeshInstance3D template)
    {
        _template = template;
    }

    public int GrasslandDensity
    {
        get => _grasslandDensity;
        set => _grasslandDensity = Math.Clamp(value, 0, 500);
    }

    public int ForestDensity
    {
        get => _forestDensity;
        set => _forestDensity = Math.Clamp(value, 0, 500);
    }

    public int JungleDensity
    {
        get => _jungleDensity;
        set => _jungleDensity = Math.Clamp(value, 0, 500);
    }

    public int SavannaDensity
    {
        get => _savannaDensity;
        set => _savannaDensity = Math.Clamp(value, 0, 500);
    }

    public void GenerateForChunk(Vector2I coord)
    {
        if (_template == null)
            return;

        var chunkPos = new Vector3(coord.X * _terrain.ChunkSize, 0, coord.Y * _terrain.ChunkSize);

        var (positions, biomes) = GetChunkVertexData(coord);
        if (positions.Count == 0)
            return;

        var validPositions = FilterPositionsByBiome(positions, biomes);
        if (validPositions.Count == 0)
            return;

        var instanceCount = Math.Min(validPositions.Count, GetBiomeDensity(coord) + _placementNoise.GetNoise2D(coord.X * 100, coord.Y * 100) * 20);
        instanceCount = Math.Max(0, instanceCount);

        var sampledPositions = SampleRandomPositions(validPositions, (int)instanceCount);

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = sampledPositions.Count
        };

        if (_template != null)
        {
            multiMesh.Mesh = _template.Mesh;
        }

        var random = new Random(coord.X * 10000 + coord.Y);
        for (int i = 0; i < sampledPositions.Count; i++)
        {
            var pos = sampledPositions[i];
            var scale = (float)(random.NextDouble() * (_maxHeight - _minHeight) + _minHeight);
            var rotation = (float)(random.NextDouble() * Math.PI * 2);

            var transform = Transform3D.Identity
                .Translated(pos)
                .Rotated(Vector3.Up, rotation)
                .Scaled(new Vector3(scale, scale, scale));

            multiMesh.SetInstanceTransform(i, transform);
        }

        var instance = new MultiMeshInstance3D
        {
            Name = $"Grass_{coord.X}_{coord.Y}",
            Multimesh = multiMesh,
            Position = chunkPos
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

    private (List<Vector3> positions, List<BiomeType> biomes) GetChunkVertexData(Vector2I coord)
    {
        var positions = new List<Vector3>();
        var biomes = new List<BiomeType>();

        var chunkSize = _terrain.ChunkSize;
        var resolution = _terrain.Resolution;
        var offsetX = coord.X * chunkSize;
        var offsetZ = coord.Y * chunkSize;

        var step = chunkSize / (float)(resolution + 1);

        for (float x = 0; x <= chunkSize; x += step)
        {
            for (float z = 0; z <= chunkSize; z += step)
            {
                var worldX = offsetX + x;
                var worldZ = offsetZ + z;

                var height = _terrain.HeightNoise.GetNoise2D(worldX, worldZ) * _terrain.Height;
                var normalizedHeight = (height / _terrain.Height + 1.0f) / 2.0f;
                var moisture = (_terrain.MoistureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;
                var temperature = (_terrain.TemperatureNoise.GetNoise2D(worldX, worldZ) + 1.0f) / 2.0f;

                var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);

                positions.Add(new Vector3(worldX, height, worldZ));
                biomes.Add(biome);
            }
        }

        return (positions, biomes);
    }

    private List<Vector3> FilterPositionsByBiome(List<Vector3> positions, List<BiomeType> biomes)
    {
        var valid = new List<Vector3>();

        for (int i = 0; i < positions.Count; i++)
        {
            var biome = biomes[i];
            if (biome == BiomeType.Grassland || biome == BiomeType.Forest ||
                biome == BiomeType.Jungle || biome == BiomeType.Savanna)
            {
                valid.Add(positions[i]);
            }
        }

        return valid;
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

    private List<Vector3> SampleRandomPositions(List<Vector3> positions, int count)
    {
        if (positions.Count <= count)
            return [.. positions];

        var random = new Random();
        var sampled = new HashSet<int>();

        while (sampled.Count < count)
        {
            var index = random.Next(positions.Count);
            sampled.Add(index);
        }

        var result = new List<Vector3>();
        foreach (var index in sampled)
        {
            result.Add(positions[index]);
        }

        return result;
    }
}
