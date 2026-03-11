using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export] private float _speed = 15.0f;
    public float Speed => _speed;

    [Export] private float _fallAcceleration = 50.0f;
    public float FallAcceleration => _fallAcceleration;

    [Export] private float _jumpImpulse = 20.0f;
    public float JumpImpulse => _jumpImpulse;

    [Export] private Camera3D _camera;

    public override void _Ready()
    {
        if (!IsInstanceValid(_camera))
        {
            _camera = GetNode<Camera3D>("CameraPivot/Camera3D");
            if (_camera == null)
            {
                GD.PrintErr(Name, ".", nameof(_Ready), " : ", "Camera3D node not found as a child of Player.");
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Turn(mouseMotion.Relative.X, mouseMotion.Relative.Y);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 direction = GetDirection();

        var newVelocity = Velocity;
        newVelocity.X = direction.X * _speed;
        newVelocity.Z = direction.Z * _speed;

        if (!IsOnFloor())
        {
            newVelocity.Y -= _fallAcceleration * (float)delta;
        }

        if (IsOnFloor() && Input.IsActionJustPressed("jump"))
        {
            newVelocity.Y = _jumpImpulse;
        }

        Velocity = newVelocity;
        MoveAndSlide();
    }

    private Vector3 GetDirection()
    {
        var forward = -Transform.Basis.Z;
        var right = Transform.Basis.X;

        var direction = Vector3.Zero;

        if (Input.IsActionPressed("move_forward"))
            direction += forward;
        if (Input.IsActionPressed("move_back"))
            direction -= forward;
        if (Input.IsActionPressed("move_left"))
            direction -= right;
        if (Input.IsActionPressed("move_right"))
            direction += right;

        return direction.Normalized();
    }

    private void Turn(float yaw, float pitch)
    {
        RotateY(Mathf.DegToRad(-yaw));

        if (_camera == null)
            return;

        var cameraRotation = _camera.RotationDegrees;
        cameraRotation.X = Mathf.Clamp(cameraRotation.X - pitch, -89, 89);
        _camera.RotationDegrees = cameraRotation;
    }
}
