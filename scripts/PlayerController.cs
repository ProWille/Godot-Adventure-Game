using Godot;

public partial class PlayerController : CharacterBody3D
{
    [Export] public int Speed { get; set; } = 15;
    [Export] public int FallAcceleration { get; set; } = 50;
    [Export] public int JumpImpulse { get; set; } = 20;

    private Vector3 _targetVelocity = Vector3.Zero;

    private Camera3D _camera;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("CameraPivot/Camera3D");
        if (_camera == null)
        {
            GD.PrintErr(Name, ".", nameof(_Ready), " : ", "Camera3D node not found as a child of Player.");
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

        _targetVelocity.X = direction.X * Speed;
        _targetVelocity.Z = direction.Z * Speed;

        if (!IsOnFloor())
        {
            _targetVelocity.Y -= FallAcceleration * (float)delta;
        }

        if (IsOnFloor() && Input.IsActionJustPressed("jump"))
        {
            _targetVelocity.Y = JumpImpulse;
        }

        Velocity = _targetVelocity;
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
