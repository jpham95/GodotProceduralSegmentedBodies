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
}
