using Godot;
using System;

[Tool, GlobalClass]
public partial class LocomotionSettings : Resource
{
    /// <summary>
    /// Enable animated procedural stepping based on ProceduralSegmenteBody position.
    /// </summary>
    [Export] public bool AnimateLocomotion = true;
    /// <summary>
    /// Distance threshold used to trigger a new step.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater")] public float StepTriggerDistance = 0.5f;
    /// <summary>
    /// Distance ahead of the rest stance position to probe for a landing along movement direction.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater")] public float StepAheadDistance = 0.7f;
    /// <summary>
    /// Multiplier applied to landing lookahead when moving backward. Used to shorten backsteps.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BackwardStepMultiplier = 0.4f;
    /// <summary>
    /// Duration of a single step in seconds.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater,suffix:sec")] public float StepDuration = 0.35f;
    /// <summary>
    /// Maximum added foot lift when the lift curve reaches a value of 1 in the world up direction.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater")] public float StepHeight = 0.5f;
    /// <summary>
    /// Foot to body orientation difference that triggers a turning step.
    /// </summary>
    [Export(PropertyHint.Range, "1,359,1,suffix:degrees")] public float TurnThresholdDegrees = 30f;
    /// <summary>
    /// Maps normalised step time to travel and rotation degrees. Use time 0..1.
    /// </summary>
    [Export] public Curve StepProgressCurve;
    /// <summary>
    /// Maps normalised step time to foot lift. Use time 0..1 with value 0 at both ends and peak of 1 for full Step Height.
    /// </summary>
    [Export] public Curve StepLiftCurve;
    
    [ExportGroup("Ground Detection")]
    /// <summary>
    /// Physics layers that the ground raycasts are tested against.
    /// </summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundCollisionMask = 1;
    /// <summary>
    /// Height above the ankle preferred position from which the ground ray begins.
    /// </summary>
    [Export(PropertyHint.Range,"0.01,10,0.01,or_greater")] public float ProbeHeight = 1f;
    /// <summary>
    /// Distance below starting point of the ground ray to test for foot placement.
    /// </summary>
    [Export(PropertyHint.Range,"0.01,10,0.01,or_greater")] public float ProbeDepth = 2f;
    /// <summary>
    /// Steepest ground surface accepted for valid foot placement.
    /// </summary>
    [Export(PropertyHint.Range,"0,89,1,suffix:degrees")] public float MaximumSlopeDegrees = 50f;
    
    [ExportGroup("Step Groups")]
    /// <summary>
    /// Check limbs in numbered groups instead of individually.
    /// </summary>
    [Export] public bool UseStepGroups = false;
    /// <summary>
    /// Number of total Step Groups to cycle through, starting at 1. Assign each grounded limb a step group between 1 and this value. 
    /// </summary>
    [Export(PropertyHint.Range, "1,10,1,or_greater")] public uint StepGroupCount = 2;
}
