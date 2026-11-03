using Godot;
using System;

[Tool, GlobalClass]
public partial class LocomotionSettings : Resource
{
    [ExportGroup("Locomotion")]
    [Export] public bool AnimateLocomotion = true;
    [Export] public float StepTriggerDistance = 0.35f;
    [Export] public float StepAheadDistance = 0.25f;
    [Export] public float BackwardStepMultiplier = 0.4f;
    [Export] public float StepDuration = 0.25f;
    [Export] public float StepHeight = 0.2f;
    [Export] public float TurnThresholdDegrees = 30f;
    [Export] public Curve StepProgressCurve;
    [Export] public Curve StepLiftCurve;
    [ExportGroup("Step Groups")] 
    [Export] public bool UseStepGroups = false;
    [Export] public uint StepGroupCount = 2;
    [ExportGroup("Ground Detection")] 
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundCollisionMask = 1;
    [Export] public float ProbeHeight = 1f;
    [Export] public float ProbeDepth = 2f;
    [Export] public float MaximumSlopeDegrees = 50f;
}
