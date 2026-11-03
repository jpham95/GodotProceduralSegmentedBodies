using Godot;
using System;

[Tool, GlobalClass]
public partial class BodyDef : Resource
{
    public enum IKTypeEnum
    {
        CCDIK,
        FABRIK,
        JacobianIK
    }
    [ExportGroup("Meshes")]
    [Export] public Mesh HeadMesh = null;
    [Export] public Mesh TorsoMesh = null;
    [Export] public Mesh JointMesh = null;
    [Export] public Godot.Collections.Array<LimbDef> Limbs = new();
    [ExportGroup("IK Settings")] 
    [Export] public IKTypeEnum IKType = IKTypeEnum.CCDIK;
    [ExportGroup("Head Settings")] 
    [Export] public Vector3 HeadAttachmentOffset = Vector3.Zero;
    [Export] public Vector3 HeadRotationDegrees = Vector3.Zero;
    [ExportGroup("Locomotion Settings")]
    [Export] public LocomotionSettings LocomotionSettings;
    [ExportGroup("Ground Detection")] 
    [Export] public uint GroundCollisionMask = 1;
    [Export] public float GroundProbeHeight = 1f;
    [Export] public float GroundProbeDepth = 2f;

    public Vector3 TorsoSize => this.TorsoMesh != null ? this.TorsoMesh.GetAabb().Size : Vector3.Zero;
}
