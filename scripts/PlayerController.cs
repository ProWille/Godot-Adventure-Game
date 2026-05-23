using Godot;

namespace AdventureGame;

public partial class PlayerController : CharacterBody3D
{
    [Export] public float Speed { get; set; } = 15.0f;
    [Export] public float FallAcceleration { get; set; } = 50.0f;
    [Export] public float JumpImpulse { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "0.0f, 90.0f, 0.1f, prefer_slider")]
    public float CameraAngleLimit { get; set; } = 60.0f;

    private TerrainController _terrainController;
    private Camera3D _camera;
    private bool _isJumping = false;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("CameraPivot/Camera3D");
        if (!IsInstanceValid(_camera))
        {
            GD.PrintErr(Name, ".", nameof(_Ready), " : ", "Camera3D node not found as a child of Player.");
        }

        _terrainController = GetTree().CurrentScene?.GetNodeOrNull<TerrainController>("TerrainController");
        if (!IsInstanceValid(_terrainController))
        {
            GD.PrintErr(Name, ".", nameof(_Ready), " : ", "TerrainController not found.");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;

        if (@event is InputEventMouseMotion mouseMotion)
            Turn(mouseMotion.Relative.X, mouseMotion.Relative.Y);

        if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Escape)
                Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            return;

        var direction = GetDirection();

        var newVelocity = Velocity;
        newVelocity.X = direction.X * Speed;
        newVelocity.Z = direction.Z * Speed;

        newVelocity.Y = IsInstanceValid(_terrainController) ? MoveAndSlideOnTerrain(delta, newVelocity.Y) : MoveAndSlideOnFloor(delta, newVelocity.Y);

        Velocity = newVelocity;
        MoveAndSlide();
    }

    private float MoveAndSlideOnTerrain(double delta, float velocityY)
    {
        float terrainY = _terrainController.GetHeight(Position.X, Position.Z) + _terrainController.PlayerOffset;

        if (_isJumping)
        {
            velocityY -= FallAcceleration * (float)delta;

            if (Position.Y <= terrainY)
            {
                Position = new Vector3(Position.X, terrainY, Position.Z);
                velocityY = 0;
                _isJumping = false;
            }
        }
        else
        {
            if (Input.IsActionJustPressed("jump"))
            {
                velocityY = JumpImpulse;
                _isJumping = true;
            }
            else
            {
                Position = new Vector3(Position.X, terrainY, Position.Z);
            }
        }

        return velocityY;
    }

    private float MoveAndSlideOnFloor(double delta, float velocityY)
    {
        if (!IsOnFloor())
        {
            velocityY -= FallAcceleration * (float)delta;
        }

        if (IsOnFloor() && Input.IsActionJustPressed("jump"))
        {
            velocityY = JumpImpulse;
        }

        return velocityY;
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

        if (!IsInstanceValid(_camera))
            return;

        var cameraRotation = _camera.RotationDegrees;
        cameraRotation.X = Mathf.Clamp(cameraRotation.X - pitch, -CameraAngleLimit, CameraAngleLimit);
        _camera.RotationDegrees = cameraRotation;
    }
}
