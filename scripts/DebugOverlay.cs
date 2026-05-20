using Godot;

namespace AdventureGame.Scripts;

public partial class DebugOverlay : CanvasLayer
{
    private Label _debugLabel;
    private Node3D _player;
    private TerrainController _terrain;

    public override void _Ready()
    {
        _debugLabel = GetNode<Label>("DebugLabel");
        _player = GetTree().CurrentScene?.GetNodeOrNull<Node3D>("Player");
        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_player) || !IsInstanceValid(_terrain) || !IsInstanceValid(_debugLabel))
            return;

        var pos = _player.GlobalPosition;
        var chunk = _terrain.GetChunkCoord(pos.X, pos.Z);
        var moisture = _terrain.GetMoisture(pos.X, pos.Z);
        var temperature = _terrain.GetTemperature(pos.X, pos.Z);
        var height = _terrain.GetHeight(pos.X, pos.Z);
        var biome = _terrain.GetBiome(moisture, temperature, height);
        _debugLabel.Text = $"""
        FPS: {Engine.GetFramesPerSecond()}
        Position: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})
        Chunk Position: ({chunk.X}, {chunk.Y})
        Moisture: {moisture}
        Temperature: {temperature}
        Height: {height}
        Biome Type: {biome}
        """;
    }
}
