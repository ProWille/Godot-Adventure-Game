using Godot;

namespace AdventureGame.Scripts;

public partial class DebugOverlay : CanvasLayer
{
    [Export(PropertyHint.Range, "0.05, 2.0, 0.05, prefer_slider")]
    public float UpdateInterval { get; set; } = 0.2f;

    private Label _debugLabel;
    private TerrainController _terrain;
    private float _elapsedTime;

    public override void _Ready()
    {
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");
        if (!IsInstanceValid(_debugLabel))
        {
            GD.PushError("DebugLabel not found in current node.");
            return;
        }

        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrain))
        {
            _debugLabel.Text = $"TerrainController node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            return;
        }

        if (!IsInstanceValid(_terrain.ActivePlayer))
        {
            _debugLabel.Text = $"Player node and Camera node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            return;
        }
    }

    public override void _Process(double delta)
    {
        _elapsedTime += (float)delta;
        if (_elapsedTime < UpdateInterval)
            return;
        _elapsedTime = 0;

        if (!IsInstanceValid(_terrain.ActivePlayer) || !IsInstanceValid(_terrain) || !IsInstanceValid(_debugLabel))
            return;

        var pos = _terrain.ActivePlayer.GlobalPosition;
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
