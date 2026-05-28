using Godot;

namespace AdventureGame.Scripts;

public partial class DebugOverlay : CanvasLayer
{
    private Label _debugLabel;
    private Node3D _player;
    private TerrainController _terrain;

    public override void _Ready()
    {
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");
        if (!IsInstanceValid(_debugLabel))
        {
            GD.PrintErr($"{Name}.{nameof(_Ready)} : DebugLabel not found in current node.");
            return;
        }

        _player = GetTree().CurrentScene?.GetNodeOrNull<Node3D>("Player");
        if (!IsInstanceValid(_player))
        {
            _debugLabel.Text = $"Player node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            return;
        }

        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrain))
        {
            _debugLabel.Text = $"TerrainController node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_player) || !IsInstanceValid(_terrain) || !IsInstanceValid(_debugLabel))
            return;

        var pos = _player.GlobalPosition;
        var chunk = _terrain.GetChunkCoord(pos.X, pos.Z);
        var moisture = _terrain.GetMoisture(pos.X, pos.Z);
        var temperature = _terrain.GetTemperature(pos.X, pos.Z);
        var height = _terrain.GetHeightmap(pos.X, pos.Z);
        var normalizedHeight = (height / _terrain.Height + 1.0f) / 2.0f;
        var biome = _terrain.GetBiome(moisture, temperature, normalizedHeight);
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
