using System;
using Godot;

namespace AdventureGame.Scripts;

public partial class PlantPlacer : DecorationGenerator
{
    protected override string InstanceName => "Plant";

    protected override bool IsValidBiome(BiomeType biome) => biome == BiomeType.Grassland || biome == BiomeType.Forest;
}
