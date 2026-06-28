using System;
using System.Collections.Generic;
using Godot;

namespace AdventureGame.Scripts;

public partial class GrassPlacer : DecorationGenerator
{
    protected override string InstanceName => "Grass";

    protected override bool IsValidBiome(BiomeType biome) => biome == BiomeType.Grassland || biome == BiomeType.Forest;
}
