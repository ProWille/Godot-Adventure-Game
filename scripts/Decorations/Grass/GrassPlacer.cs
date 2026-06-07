using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class GrassPlacer : DecorationGenerator
{
    protected override string InstanceName => "Grass";

    public override void Initialize(TerrainController terrain)
    {
        base.Initialize(terrain);

        _template = terrain.GrassTemplate;
    }

    protected override bool IsValidBiome(BiomeType biome)
    {
        return biome == BiomeType.Grassland || biome == BiomeType.Forest;
    }
}
