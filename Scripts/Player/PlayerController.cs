using Godot;
using System;

[GlobalClass]
public partial class PlayerController : CharacterBody3D
{
	[Export] private float MoveSpeed = 5.0f;
	[Export] private float MoveDeceleration = 10f;
	[Export] private NodePath InputManagerPath = null;
	[ExportGroup("Camera")] 
	[Export] private NodePath CameraPath;
	[Export] private float MouseSens = 0.0016f;
	[Export] private float CameraLookDownLimit = 80f;
	[Export] private float CameraLookUpLimit = 70f;

	private Vector3 DesiredVelocity = Vector3.Zero;
	private Camera3D Camera;
	private InputManager _InputManager;
	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		Camera = GetNodeOrNull<Camera3D>(CameraPath);
		if (Camera == null)
		{
			GD.PrintErr($"No Camera3D found at ${CameraPath}");
		}
		_InputManager = GetNodeOrNull<InputManager>(InputManagerPath);
		if (_InputManager == null)
		{
			GD.PrintErr($"No InputManager found at ${InputManagerPath}");
		}
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (inputEvent.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			return;
		}
		
		if (inputEvent is InputEventMouseMotion &&
		    Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			Transform2D viewportTransform = GetTree().Root.GetFinalTransform();
			InputEventMouseMotion mouseEvent = (InputEventMouseMotion)inputEvent.XformedBy(viewportTransform);
			Vector2 mouseMotion = mouseEvent.Relative;
			RotateCamera(mouseMotion * MouseSens);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_InputManager == null)
		{
			return;
		}
		
		SetDesiredVelocity(MoveSpeed);
		ApplyHorizontalVelocity((float)delta);
		MoveAndSlide();
	}

	private void SetDesiredVelocity(float speed)
	{
		Vector2 inputDirection = _InputManager.GetMoveInput();
		if (inputDirection == Vector2.Zero)
		{
			DesiredVelocity.X = 0f;
			DesiredVelocity.Z = 0f;
			return;
		}

		Vector3 direction = ComputeCameraRelativeDirection(inputDirection);
		DesiredVelocity.X = direction.X * speed;
		DesiredVelocity.Z = direction.Z * speed;
	}

	private void ApplyHorizontalVelocity(float deltaTime)
	{
		Vector3 currentVelocity = Velocity;
		Vector3 currentHorizontalVelocity = new Vector3(currentVelocity.X, 0f, currentVelocity.Z);
		Vector3 targetHorizontalVelocity = new Vector3(DesiredVelocity.X, 0f, DesiredVelocity.Z);

		currentHorizontalVelocity = currentHorizontalVelocity.Lerp(
			targetHorizontalVelocity,
			float.Clamp(MoveDeceleration * deltaTime, 0f, 1f)
		);
		SetVelocity(new Vector3(currentHorizontalVelocity.X, 0f, currentHorizontalVelocity.Z));
	}

	private Vector3 ComputeCameraRelativeDirection(Vector2 inputDirection)
	{
		if (inputDirection == Vector2.Zero)
		{
			return Vector3.Zero;
		}

		if (Camera == null)
		{
			// Fallback to player basis
			Vector3 forward = -GlobalTransform.Basis.Z;
			forward.Y = 0f;
			forward = forward.Normalized();

			Vector3 right = GlobalTransform.Basis.X;
			right.Y = 0f;
			right = right.Normalized();

			return (forward * inputDirection.Y + right * inputDirection.X).Normalized();
		}

		Vector3 cameraForward = -Camera.GlobalTransform.Basis.Z;
		cameraForward.Y = 0f;
		cameraForward = cameraForward.Normalized();

		Vector3 cameraRight = Camera.GlobalTransform.Basis.X;
		cameraRight.Y = 0f;
		cameraRight = cameraRight.Normalized();

		return (cameraForward * inputDirection.Y + cameraRight * inputDirection.X).Normalized();
	}

	private void RotateCamera(Vector2 mouseMotion)
	{
		RotateY(-mouseMotion.X);
	}
}