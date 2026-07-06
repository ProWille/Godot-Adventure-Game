using Godot;

namespace AdventureGame.Scripts;

public partial class DebugOverlay : CanvasLayer
{
    [Export(PropertyHint.Range, "0.05f, 2.0f, 0.05f, prefer_slider")]
    private float UpdateInterval { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.01f, prefer_slider")]
    private float BackgroundTransparency { get; set; } = 0.5f;
    [Export] private Vector2 _margin = new(10, 10);

    private Label _debugLabel;
    private ColorRect _background;
    private TerrainController _terrain;
    private float _elapsedTime;

    private Vector2 BackgroundSize => _debugLabel.GetCombinedMinimumSize() + _margin;

    private void CreateBackground()
    {
        _background = new ColorRect
        {
            Color = new(0.0f, 0.0f, 0.0f, BackgroundTransparency),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = _margin
        };
        AddChild(_background);
    }

    private void CreateLabel()
    {
        _debugLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = _margin * 0.5f
        };
        _background.AddChild(_debugLabel);
    }

    private void ErrorMessage(string message)
    {
        _debugLabel.Text = message;
        _debugLabel.LabelSettings.FontColor = Colors.Red;
        _background.Size = BackgroundSize;
    }

    public override void _Ready()
    {
        CreateBackground();
        CreateLabel();

        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrain))
        {
            ErrorMessage($"TerrainController node not found in current scene.");
            return;
        }

        if (!IsInstanceValid(_terrain.ActivePlayer))
        {
            ErrorMessage($"Player node and Camera node not found in current scene.");
            return;
        }
    }

    public override void _Process(double delta)
    {
        _elapsedTime += (float)delta;
        if (_elapsedTime < UpdateInterval) return;
        _elapsedTime = 0;

        if (!IsInstanceValid(_terrain.ActivePlayer) || !IsInstanceValid(_terrain) || !IsInstanceValid(_debugLabel))
            return;

        var pos = _terrain.ActivePlayer.GlobalPosition;
        var chunk = _terrain.GetChunkCoord(pos.X, pos.Z);
        var moisture = _terrain.GetMoisture(pos.X, pos.Z);
        var temperature = _terrain.GetTemperature(pos.X, pos.Z);
        var height = _terrain.GetNormalizedHeight(pos.X, pos.Z);
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

        _background.Size = BackgroundSize;
    }
}
