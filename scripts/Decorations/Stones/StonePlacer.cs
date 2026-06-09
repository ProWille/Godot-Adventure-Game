using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class StonePlacer : DecorationGenerator
{
    protected override string InstanceName => "Stone";

    public override void Initialize(TerrainController terrain)
    {
        base.Initialize(terrain);

        _template = terrain.StoneTemplate;
    }

    protected override bool IsValidBiome(BiomeType biome)
    {
        return biome == BiomeType.Grassland || biome == BiomeType.Forest;
    }
}
