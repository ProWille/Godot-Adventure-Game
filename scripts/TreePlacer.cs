using System;
using System.Collections.Generic;
using Godot;

internal class TreePlacer
{
    private MeshInstance3D _template;

    private readonly int _forestDensity;
    private readonly int _jungleDensity;

    private readonly float _minHeightThreshold;
    private readonly float _maxHeightThreshold;
    private readonly float _minScale;
    private readonly float _maxScale;
    private readonly int _renderDistance;

    private readonly int _densityNoiseScale;
    private readonly int _densityNoiseAmplitude;
    private readonly int _randomSeedBase;
    private readonly float _placementFrequency;

    private readonly FastNoiseLite _placementNoise;
    private readonly Dictionary<Vector2I, MultiMeshInstance3D> _instances = [];
    private readonly TerrainController _terrain;
    private readonly Node3D _parent;
    private readonly object _lock = new();

    public TreePlacer(
        TerrainController terrain,
        Node3D parent,
        int forestDensity,
        int jungleDensity,
        float minHeightThreshold,
        float maxHeightThreshold,
        float minScale,
        float maxScale,
        int renderDistance,
        int densityNoiseScale,
        int densityNoiseAmplitude,
        int randomSeedBase,
        float placementFrequency)
    {
        _terrain = terrain;
        _parent = parent;
        _forestDensity = forestDensity;
        _jungleDensity = jungleDensity;
        _minHeightThreshold = minHeightThreshold;
        _maxHeightThreshold = maxHeightThreshold;
        _minScale = minScale;
        _maxScale = maxScale;
        _renderDistance = renderDistance;
        _densityNoiseScale = densityNoiseScale;
        _densityNoiseAmplitude = densityNoiseAmplitude;
        _randomSeedBase = randomSeedBase;
        _placementFrequency = placementFrequency;
        _placementNoise = new FastNoiseLite
        {
            Seed = new Random().Next() * 1000,
            Frequency = _placementFrequency
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

        var chunkSize = _terrain.ChunkSize;

        var centerHeight = _terrain.GetHeight(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);
        var normalizedCenterHeight = (centerHeight / _terrain.Height + 1.0f) / 2.0f;

        var moisture = _terrain.GetMoisture(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);
        var temperature = _terrain.GetTemperature(coord.X + chunkSize / 2f, coord.Y + chunkSize / 2f);

        var biome = _terrain.GetBiome(moisture, temperature, normalizedCenterHeight);

        if (biome != BiomeType.Forest && biome != BiomeType.Jungle)
            return;

        if (normalizedCenterHeight < _minHeightThreshold || normalizedCenterHeight > _maxHeightThreshold)
            return;

        var density = biome == BiomeType.Jungle ? _jungleDensity : _forestDensity;
        var adjustedDensity = density + (int)(_placementNoise.GetNoise2D(coord.X * _densityNoiseScale, coord.Y * _densityNoiseScale) * _densityNoiseAmplitude);
        adjustedDensity = Math.Max(0, adjustedDensity);

        var positions = SamplePositions(coord, adjustedDensity);
        if (positions.Count == 0)
            return;

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = positions.Count
        };

        if (_template != null)
        {
            multiMesh.Mesh = _template.Mesh;
        }

        var random = new Random(coord.X * 10000 + coord.Y + _randomSeedBase);
        for (int i = 0; i < positions.Count; i++)
        {
            var rotation = (float)(random.NextDouble() * Math.PI * 2);
            var scale = (float)(random.NextDouble() * (_maxScale - _minScale) + _minScale);
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
            Name = $"Trees_{chunkPos.X}_{chunkPos.Y}",
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

                if (normalizedHeight < _minHeightThreshold || normalizedHeight > _maxHeightThreshold)
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
