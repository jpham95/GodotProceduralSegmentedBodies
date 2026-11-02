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

    [Export] public string LimbID = "";
    [Export] public Godot.Collections.Array<SegmentDef> Segments = new();
    [Export] public Vector3 AttachmentPosition = Vector3.Zero;
    [Export] public Vector3 AttachmentRotationDegrees = Vector3.Zero;
    [Export] public LimbPreset Preset = LimbPreset.Custom;
    [ExportGroup("Magnet Targets")] 
    [Export] public bool UseMagnetTargets = false;
    [ExportGroup("Arm Settings")]
    [Export] public ArmSide ArmAttachSide = ArmSide.Left;
    [ExportGroup("Custom Limb Settings")] 
    [Export] private Vector3 CustomLimbDirection = Vector3.Down;
    [Export] public bool CustomGroundFoot = true;
    [ExportGroup("End Mesh Settings")]
    [Export] public Mesh EndMesh;
    [Export] public Vector3 EndMeshOffset = Vector3.Zero;
    [Export] public Vector3 EndMeshRotationDegrees = Vector3.Zero;
    [Export] public float GroundClearance = 0.01f;
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
