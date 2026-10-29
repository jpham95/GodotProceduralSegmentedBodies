using Godot;
using System;

[Tool, GlobalClass]
public partial class LocomotionLimbDef : Resource
{
    [Export] public LimbDef Definition;
    [Export] public NodePath StanceRayPath;
    [Export] public NodePath LandingRayPath;
    [Export] public NodePath TargetPath;
    [Export] public Vector3 RestPosition;
    public Vector3 PreviousRestPosition;

    [Export] public float PhaseOffset = 0f;
}
