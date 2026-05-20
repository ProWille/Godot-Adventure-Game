using Godot;

namespace AdventureGame.Scripts;

public partial class PositionDisplay : CanvasLayer
{
    private Label _positionLabel;
    private Node3D _player;
    private TerrainController _terrain;

    public override void _Ready()
    {
        _positionLabel = GetNode<Label>("PositionLabel");
        _player = GetTree().CurrentScene?.GetNodeOrNull<Node3D>("Player");
        _terrain = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_player) || !IsInstanceValid(_terrain) || !IsInstanceValid(_positionLabel))
            return;

        var pos = _player.GlobalPosition;
        var chunk = _terrain.GetChunkCoord(pos.X, pos.Z);
        _positionLabel.Text = $"Position: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})\nChunk Position: ({chunk.X}, {chunk.Y})";
    }
}
