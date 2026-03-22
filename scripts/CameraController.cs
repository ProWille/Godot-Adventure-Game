using System;
using Godot;

public partial class CameraController : Camera3D
{
    [Export(PropertyHint.Range, "0.1f, 10.0f, 0.1f, prefer_slider")]
    private float _speed = 5.0f;
    public float Speed
    {
        get => _speed;
        private set => _speed = Math.Clamp(value, 0.1f, 10.0f);
    }

    [Export(PropertyHint.Range, "0.1f, 1.0f, 0.1f, prefer_slider")]
    private float _sensitivity = 0.5f;

    [Export(PropertyHint.Range, "0.0f, 90.0f, 0.1f, prefer_slider")]
    private float _cameraAngleLimit = 60.0f;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Turn(mouseMotion.Relative.X * _sensitivity, mouseMotion.Relative.Y * _sensitivity);
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.WheelUp:
                    Speed += 0.1f;
                    return;
                case MouseButton.WheelDown:
                    Speed -= 0.1f;
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
        Position += direction * _speed;
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
        cameraRotation.X = Mathf.Clamp(cameraRotation.X - pitch, -_cameraAngleLimit, _cameraAngleLimit);
        RotationDegrees = cameraRotation;
    }
}
