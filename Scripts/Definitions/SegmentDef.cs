using Godot;
using System;
using System.Numerics;
using Vector3 = Godot.Vector3;

[Tool, GlobalClass]
public partial class SegmentDef : Node
{
    [Export] public float Length = 1f;
    [Export] public Vector3 Scale = Vector3.One; 
    [Export] public Vector3 RestRotationDegrees = Vector3.Zero;
    [Export] public Mesh mesh = null;
    [Export] public Vector3 MeshOffset = Vector3.Zero;
    [Export] public Vector3 MeshRotationDegrees = Vector3.Zero;
}
