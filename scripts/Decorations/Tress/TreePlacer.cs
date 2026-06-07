using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class TreePlacer : DecorationGenerator
{
    protected override string InstanceName => "Tree";

    public override void Initialize(TerrainController terrain)
    {
        base.Initialize(terrain);

        _template = terrain.TreeTemplate;
    }

    protected override void OnPositionsSampled(Vector2I coord, ref List<Vector3> positions)
    {
        var adjustedDensity = ForestDensity + (int)(_placementNoise.GetNoise2D(coord.X * DensityNoiseScale, coord.Y * DensityNoiseScale) * DensityNoiseAmplitude);
        adjustedDensity = Math.Max(0, adjustedDensity);

        if (positions.Count > adjustedDensity)
            positions = AdjustSampledPositions(positions, adjustedDensity);
    }

    protected override bool ShouldGenerateForChunk(Vector2I coord)
    {
        var chunkSize = _terrain.ChunkSize;
        var centerX = coord.X * chunkSize + chunkSize / 2f;
        var centerZ = coord.Y * chunkSize + chunkSize / 2f;

        var centerHeight = _terrain.GetHeightmap(centerX, centerZ);
        var normalizedCenterHeight = (centerHeight / _terrain.Height + 1.0f) / 2.0f;

        if (normalizedCenterHeight < MinHeightThreshold || normalizedCenterHeight > MaxHeightThreshold)
            return false;

        var biome = _terrain.GetBiome(centerX, centerZ);
        return biome == BiomeType.Forest;
    }

    protected override bool IsValidBiome(BiomeType biome)
    {
        return biome == BiomeType.Forest;
    }

    private static List<Vector3> AdjustSampledPositions(List<Vector3> positions, int count)
    {
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
