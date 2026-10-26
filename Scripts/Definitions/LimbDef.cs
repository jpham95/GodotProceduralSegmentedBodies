using Godot;
using System;

[Tool, GlobalClass]
public partial class LimbDef : Resource
{
    [Export] public string LimbID = "";
    [Export] public bool LockIKToAxis = false;
    [Export] public SkeletonModifier3D.RotationAxis IKLockAxis = SkeletonModifier3D.RotationAxis.X;
    [Export] public Vector3 AttachmentPosition = Vector3.Zero;
    [Export] public Vector3 AttachmentRotationDegrees = Vector3.Zero;
    [Export] public Godot.Collections.Array<SegmentDef> Segments = new();
    [Export] public Mesh EndMesh;
    [Export] public Vector3 EndMeshOffset = Vector3.Zero;
    [Export] public bool GroundFoot = true;
    [Export] public float GroundClearance = 0.01f;
    [Export] public bool AlignFootToGround = true;
    [Export] public Vector3 EndMeshRotationDegrees = Vector3.Zero;
    public float EndMeshHeight => EndMesh.GetAabb().Size.Y;
    public Vector3 LimbDirection => this.Segments.Count > 0 ? this.Segments[0].LimbDirection : Vector3.Down;   
    public float AnkleToSoleDistance
    {
        get
        {
            if (EndMesh == null)
            {
                return 0f;
            }

            Basis rotation = Basis.FromEuler(EndMeshRotationDegrees * (Mathf.Pi / 180f));

            Transform3D meshTransform = new Transform3D(rotation, EndMeshOffset);

            Aabb bounds = meshTransform * EndMesh.GetAabb();

            return Mathf.Max(0f, -bounds.Position.Y);
        }
    }
}
