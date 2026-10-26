using Godot;
using System;

[Tool, GlobalClass]
public partial class BodyDef : Resource
{
    [Export] public Mesh BodyMesh = null;
    [Export] public Mesh JointMesh = null;
    [Export] public Mesh HeadMesh = null;
    [Export] public Godot.Collections.Array<LimbDef> Limbs = new();

    [ExportGroup("Ground Detection")] 
    [Export]
    public uint GroundCollisionMask = 1;

    [Export] public float GroundProbeHeight = 1f;
    [Export] public float GroundProbeDepth = 2f;

    public Vector3 TorsoSize => this.BodyMesh != null ? this.BodyMesh.GetAabb().Size : Vector3.Zero;
}
