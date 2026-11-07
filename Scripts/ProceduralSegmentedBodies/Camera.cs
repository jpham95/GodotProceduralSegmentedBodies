using Godot;

public partial class Camera : Camera3D
{
    [Export] public Node3D Target;
    [Export] public Vector3 Offset = Vector3.Zero;
    [Export] public float Distance = 3f;
    [Export] public float FollowSpeed = 6f;

    private Vector3 _viewDirection;

    public override void _Ready()
    {
        if (!IsInstanceValid(Target))
        {
            GD.PrintErr("Assign a target to the camera in the inspector.");
            SetProcess(false);
            return;
        }

        Vector3 focus = Target.GlobalPosition + Offset;
        Vector3 offset = GlobalPosition - focus;

        _viewDirection = offset.IsZeroApprox()
            ? new Vector3(0f, 0.4f, 1f).Normalized()
            : offset.Normalized();
        
        MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(Target))
        {
            return;
        }

        Vector3 focus = Target.GlobalPosition + Offset;
        Vector3 desiredPosition = focus + _viewDirection * Distance;

        float weight = 1f - Mathf.Exp(-FollowSpeed * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(desiredPosition, weight);

        Vector3 toFocus = focus - GlobalPosition;
        if (toFocus.IsZeroApprox())
        {
            return;
        }

        Vector3 up = Mathf.Abs(toFocus.Normalized().Dot(Vector3.Up)) > 0.999f
            ? Vector3.Forward
            : Vector3.Up;
        
        LookAt(focus, up);
    }
}
