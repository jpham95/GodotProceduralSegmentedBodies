using Godot;
using System;

[Tool, GlobalClass]
public partial class BodyDef : Node
{
    [Export] public Mesh BodyMesh = null;
    [Export] public Mesh JointMesh = null;
    [Export] public Mesh HeadMesh = null;
    [Export] public Godot.Collections.Array<LimbDef> Limbs = new();
}
