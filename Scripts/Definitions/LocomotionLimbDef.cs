using Godot;
using System;

[Tool, GlobalClass]
public partial class LocomotionLimbDef : Resource
{
    /// <summary>
    /// Source limb definition associated with this resource.
    /// </summary>
    [Export] public LimbDef Definition;
    [Export] public NodePath StanceRayPath;
    [Export] public NodePath LandingRayPath;
    [Export] public NodePath TargetPath;
    [Export] public Vector3 RestPosition;
    public Vector3 PreviousRestPosition;
}
