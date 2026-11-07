using Godot;
using System;

[GlobalClass]
public partial class InputManager : Node
{
	private Vector2 MoveVector = Vector2.Zero;
	private float MoveLeftStrength = 0f;
	private float MoveRightStrength = 0f;
	private float MoveForwardStrength = 0f;
	private float MoveBackwardStrength = 0f;

	private bool RunReleased = false;
	private bool RunHeld = false;

	[ExportGroup("Input Actions")]
	[Export] private StringName MoveLeftAction = "MoveLeft";
	[Export] private StringName MoveRightAction = "MoveRight";
	[Export] private StringName MoveForwardAction = "MoveForward";
	[Export] private StringName MoveBackwardAction = "MoveBackward";

	public Vector2 GetMoveInput()
	{
		return Input.GetVector(
			MoveLeftAction, 
			MoveRightAction, 
			MoveBackwardAction, 
			MoveForwardAction
		);
	}

	public float GetMoveLeft()
	{
		return Input.GetActionStrength(MoveLeftAction);
	}
	
	public float GetMoveRight()
	{
		return Input.GetActionStrength(MoveRightAction);
	}
	
	public float GetMoveForward()
	{
		return Input.GetActionStrength(MoveForwardAction);
	}
	
	public float GetMoveBackward()
	{
		return Input.GetActionStrength(MoveBackwardAction);
	}
}