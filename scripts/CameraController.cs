using System;
using Godot;

public partial class CameraController : Camera3D
{
    [Export(PropertyHint.Range, "0, 100, 1, prefer_slider")]
    private int _speed = 10;
    public int Speed
    {
        get => _speed;
        private set
        {
            _speed = Math.Clamp(value, 0, 100);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Turn(mouseMotion.Relative.X, mouseMotion.Relative.Y);
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && !mouseButton.IsEcho())
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.WheelUp:
                    Speed++;
                    return;
                case MouseButton.WheelDown:
                    Speed--;
                    return;
                default:
                    return;
            }
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Space)
            {
                GetTree().ReloadCurrentScene();
            }
        }
    }

    public override void _Process(double delta)
    {
        var direction = GetDirection();
        Position += direction * Speed;
    }

    private Vector3 GetDirection()
    {
        var right = Transform.Basis.X;
        var up = Transform.Basis.Y;
        var forward = -Transform.Basis.Z;

        var direction = Vector3.Zero;

        if (Input.IsActionPressed("move_forward"))
            direction += forward;
        if (Input.IsActionPressed("move_back"))
            direction -= forward;
        if (Input.IsActionPressed("move_left"))
            direction -= right;
        if (Input.IsActionPressed("move_right"))
            direction += right;
        if (Input.IsActionPressed("move_up"))
            direction += up;
        if (Input.IsActionPressed("move_down"))
            direction -= up;

        return direction.Normalized();
    }

    private void Turn(float yaw, float pitch)
    {
        RotateY(Mathf.DegToRad(-yaw));

        var cameraRotation = RotationDegrees;
        cameraRotation.X = Mathf.Clamp(cameraRotation.X - pitch, -30, 30);
        RotationDegrees = cameraRotation;
    }
}
