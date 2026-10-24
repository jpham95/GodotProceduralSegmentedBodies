using Godot;
using System;

[Tool, GlobalClass]
public partial class LimbDef : Node
{
    [Export] public string LimbID = "";
    [Export] public Vector3 AttachmentPosition = Vector3.Zero;
    [Export] public Vector3 AttachmentRotationDegrees = Vector3.Zero;
    [Export] public Godot.Collections.Array<SegmentDef> Segments = new();
    [Export] public Mesh EndMesh;
}
