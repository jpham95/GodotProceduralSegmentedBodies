using Godot;
using System;

[Tool, GlobalClass]
public partial class LimbDef : Resource
{
    public enum LimbPreset
    {
        Leg,
        Arm,
        Custom
    }

    public enum ArmSide
    {
        Left,
        Right
    }

    /// <summary>
    /// Unique name used for this limb's generated bones and nodes.
    /// </summary>
    [Export] public string LimbID = "";
    /// <summary>
    /// Segments ordered from the torso outward - each segment supplies its mesh, length, rest rotation and joint limitation settings.
    /// </summary>
    [Export] public Godot.Collections.Array<SegmentDef> Segments = new();
    /// <summary>
    /// Additional offset from the attachment location, in torso-local coordinates.
    /// </summary>
    [Export] public Vector3 AttachmentPosition = Vector3.Zero;
    /// <summary>
    /// Rotation of the entire limb at its torso attachment location, in degrees.
    /// </summary>
    [Export] public Vector3 AttachmentRotationDegrees = Vector3.Zero;
    /// <summary>
    /// Preset option for Legs/Arms/Custom -
    /// Arms attach to a side selected in Arm Settings with ungrounded ends,
    /// Legs attach to the bottom of the torso with grounded ends,
    /// Custom uses custom limb settings. 
    /// </summary>
    [Export] public LimbPreset Preset = LimbPreset.Custom;
    /// <summary>
    /// Adds an intermediate IK target to influence limb bending. Use only for limbs with more than 2 segments.
    /// Adjust before final usage - incorrect placement can have undesirable results.
    /// </summary>
    [Export] public bool UseMagnetTargets = false;
    [ExportGroup("Arm Settings")]
    /// <summary>
    /// Torso side used by the Arm preset.
    /// </summary>
    [Export] public ArmSide ArmAttachSide = ArmSide.Left;
    [ExportGroup("Leg Settings")] 
    /// <summary>
    /// Group number used when step groups are enabled. Use a value from 1 to Step Group Count (defined in Locomotion Settings).
    /// </summary>
    [Export(PropertyHint.Range,"1,10,1,or_greater")] public uint StepGroup = 1;
    [ExportGroup("Custom Limb Settings")]
    /// <summary>
    /// Custom attachment location expressed as multiples of the torso half-size.
    /// E.g.: (0, -1, 0) attaches at the bottom-center.
    /// </summary>
    [Export] private Vector3 CustomLimbDirection = Vector3.Down;
    /// <summary>
    /// Enable grounded end and procedural stepping (if enabled in Locomotion Settings) for this limb.
    /// </summary>
    [Export] public bool CustomGroundFoot = true;
    
    [ExportGroup("End Mesh Settings")]
    /// <summary>
    /// Optional foot or hand mesh attached to the limb's end bone.
    /// </summary>
    [Export] public Mesh EndMesh;
    /// <summary>
    /// Mesh offset in end-bone local coordinates. For grounded ends ensure the bottom of the mesh is below the "ankle".
    /// </summary>
    [Export] public Vector3 EndMeshOffset = Vector3.Zero;
    /// <summary>
    /// Mesh rotation relative to the end bone, in degrees.
    /// </summary>
    [Export] public Vector3 EndMeshRotationDegrees = Vector3.Zero;
    /// <summary>
    /// Mesh scale applied during generation.
    /// </summary>
    [Export(PropertyHint.Link)] public Vector3 EndMeshScale = Vector3.One;
    /// <summary>
    /// Extra distance between the contact and the ground, measured against the ground's normal during locomotion.
    /// </summary>
    [Export(PropertyHint.Range,"0,0.5,0.001,or_greater")] public float GroundClearance = 0.01f;
    
    
    public bool AlignFootToGround => GroundFoot;
    public float EndMeshHeight => EndMesh.GetAabb().Size.Y;
    public Vector3 LimbDirection => this.Segments.Count > 0 ? this.Segments[0].LengthDirection : Vector3.Down;   
    public float AnkleToContactDistance
    {
        get
        {
            if (EndMesh == null)
            {
                return 0f;
            }

            Basis rotation = Basis.FromEuler(Util.Vector3DegToRad(EndMeshRotationDegrees));

            Transform3D meshTransform = new Transform3D(rotation, EndMeshOffset);

            Aabb bounds = meshTransform * EndMesh.GetAabb();

            return Mathf.Max(0f, -bounds.Position.Y);
        }
    }

    public Vector3 AttachmentDirection => Preset switch
    {
        LimbPreset.Custom => CustomLimbDirection,
        LimbPreset.Leg => Vector3.Down,
        LimbPreset.Arm => ArmAttachSide == ArmSide.Left ? Vector3.Left : Vector3.Right,
        _ => Vector3.Down
    };

    public bool GroundFoot => Preset switch
    {
        LimbPreset.Leg => true,
        LimbPreset.Arm => false,
        LimbPreset.Custom => CustomGroundFoot,
        _ => false
    };
    
}
