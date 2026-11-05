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
    /// <summary>
    /// Optional head mesh, leave empty to generate no head.
    /// </summary>
    [Export] public Mesh HeadMesh = null;
    
    /// <summary>
    /// Required torso mesh. Its bounds decide torso size and starting locations of limb attachments
    /// </summary>
    [Export] public Mesh TorsoMesh = null;
    
    /// <summary>
    /// Optional mesh placed at generated joints, leave empty to omit joint visuals
    /// </summary>
    [Export] public Mesh JointMesh = null;
    
    /// <summary>
    /// Limb definitions to generate. Each limb requires a unique non-empty Limb ID.
    /// </summary>
    [Export] public Godot.Collections.Array<LimbDef> Limbs = new();
    
    /// <summary>
    /// Solver used to position limbs. Different solvers produce different poses with the same targets and limitations.
    /// </summary>
    [Export] public IKTypeEnum InverseKinematicType = IKTypeEnum.CCDIK;
    
    [ExportGroup("Head Settings")]
    /// <summary>
    /// Offset from the top center of the torso in torso-local coordinates.
    /// </summary>
    [Export] public Vector3 HeadAttachmentOffset = Vector3.Zero;
    /// <summary>
    /// Rotation of the head mesh relative to its attachment point, in degrees.
    /// </summary>
    [Export] public Vector3 HeadRotationDegrees = Vector3.Zero;
    
    [ExportGroup("Locomotion Settings")]
    /// <summary>
    /// Shared, saveable movement animation and ground-detection settings.
    /// </summary>
    [Export] public LocomotionSettings LocomotionSettings;

    public Vector3 TorsoSize => this.TorsoMesh != null ? this.TorsoMesh.GetAabb().Size : Vector3.Zero;
}
