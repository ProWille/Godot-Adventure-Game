using Godot;

namespace AdventureGame.Scripts;

public partial class DebugOverlay : CanvasLayer
{
    [Export(PropertyHint.Range, "0.05, 2.0, 0.05, prefer_slider")]
    public float UpdateInterval { get; set; } = 0.2f;

    [Export] public bool EnableBackground { get; set; } = true;
    [Export(PropertyHint.Range, "0.0f, 1.0f, 0.01f, prefer_slider")]
    public float BackgroundTransparency { get; set; } = 0.5f;

    private Label _debugLabel;
    private ColorRect _background;
    private TerrainController _terrain;
    private float _elapsedTime;

    private Vector2 BackgroundPosition => _debugLabel.Position - new Vector2(8, 4);
    private Vector2 BackgroundSize => _debugLabel.Size + new Vector2(16, 8);

    private void CreateBackground()
    {
        _background = new ColorRect
        {
            Color = new(0.0f, 0.0f, 0.0f, BackgroundTransparency),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_background);
        MoveChild(_background, 0);
    }

    private void UpdateBackground()
    {
        if (!EnableBackground) return;
        _background.Position = BackgroundPosition;
        _background.Size = BackgroundSize;
    }

    public override void _Ready()
    {
        _debugLabel = GetNodeOrNull<Label>("DebugLabel");
        if (!IsInstanceValid(_debugLabel))
        {
            GD.PushError("DebugLabel not found in current node.");
            return;
        }

        if (EnableBackground) CreateBackground();

        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrain))
        {
            _debugLabel.Text = $"TerrainController node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            UpdateBackground();
            return;
        }

        if (!IsInstanceValid(_terrain.ActivePlayer))
        {
            _debugLabel.Text = $"Player node and Camera node not found in current scene.";
            _debugLabel.LabelSettings.FontColor = Colors.Red;
            UpdateBackground();
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

        UpdateBackground();
    }
}
