using Godot;

namespace AdventureGame.Scripts;

public abstract partial class BiomeGenerator : Resource
{
    [Export] public FastNoiseLite HeightmapNoise { get; set; }
    [Export] public FastNoiseLite DecorationNoise { get; set; }
    [Export] public Curve DetailWeight { get; set; }
    [Export] public Curve ErosionWeight { get; set; }
}
