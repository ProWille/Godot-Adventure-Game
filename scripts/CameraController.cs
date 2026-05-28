using System;
using Godot;

namespace AdventureGame.Scripts;

public partial class CameraController : Camera3D
{
    private float _speed = 5.0f;
    [Export(PropertyHint.Range, "0.1f, 10.0f, 0.1f, prefer_slider")]
    public float Speed { get => _speed; set => _speed = Math.Clamp(value, 0.1f, 10.0f); }

    [Export(PropertyHint.Range, "0.1f, 1.0f, 0.1f, prefer_slider")]
    public float Sensitivity { get; set; } = 0.5f;

    [Export(PropertyHint.Range, "0.0f, 90.0f, 0.1f, prefer_slider")]
    public float CameraAngleLimit { get; set; } = 60.0f;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                Speed += 0.1f;

            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                Speed -= 0.1f;
        }

        if (@event is InputEventMouseMotion mouseMotion)
            Turn(mouseMotion.Relative.X * Sensitivity, mouseMotion.Relative.Y * Sensitivity);

        if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Space)
                GetTree().ReloadCurrentScene();

            else if (key.Keycode == Key.Escape)
                Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _Process(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;

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
        cameraRotation.X = Mathf.Clamp(cameraRotation.X - pitch, -CameraAngleLimit, CameraAngleLimit);
        RotationDegrees = cameraRotation;
    }
}
