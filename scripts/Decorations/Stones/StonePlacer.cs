using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class StonePlacer : DecorationGenerator
{
    protected override string InstanceName => "Stone";

    protected override bool IsValidBiome(BiomeType biome) => biome == BiomeType.Grassland || biome == BiomeType.Forest;

    public override void Initialize(TerrainController terrain)
    {
        base.Initialize(terrain);

        _template = terrain.StoneTemplate;
    }
}
