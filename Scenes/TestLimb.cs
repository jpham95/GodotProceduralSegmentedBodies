using Godot;
using System;
using System.Numerics;
using Quaternion = Godot.Quaternion;
using Vector3 = Godot.Vector3;

[Tool]
public partial class TestLimb : Skeleton3D
{
    [ExportToolButton("Generate / Regenerate")] 
    public Callable CreateLimbButton => Callable.From(CreateLimb);
    [ExportToolButton("Clear")] 
    public Callable ClearButton => Callable.From(Clear);

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        ClearBones();
    }
    private void CreateLimb()
    {
        Clear();
        
        ClearBones();

        int hipIdx = AddBone("Hip");
        int kneeIdx = AddBone("Knee");
        int ankleIdx = AddBone("Ankle");
        
        SetBoneParent(kneeIdx, hipIdx);
        SetBoneParent(ankleIdx, kneeIdx);
        
        // rest transforms relative to bone parent
        SetBoneRest(hipIdx, new Transform3D(Basis.Identity, new Vector3(0, 2, 0)));
        SetBoneRest(kneeIdx, new Transform3D(Basis.Identity, new Vector3(0, -1, 0.2f)));
        SetBoneRest(ankleIdx, new Transform3D(Basis.Identity, new Vector3(0, -1, -0.2f)));
        
        ResetBonePoses();
        
        AddLimbMesh(hipIdx, GetBoneRest(kneeIdx).Origin);
        AddLimbMesh(kneeIdx, GetBoneRest(ankleIdx).Origin);
        AddLimbMesh(ankleIdx, Vector3.Zero);
        // bake to combined arraymesh?

        Marker3D targetNode = new Marker3D
        {
            Name = "TargetNode",
            Position = GetBoneGlobalRest(ankleIdx).Origin
        };
        
        AddChild(targetNode);
        targetNode.Owner = Owner;

        Fabrik3D IKNode = new Fabrik3D
        {
            Name = "FABRIK3D"
        };
        
        AddChild(IKNode);
        
        IKNode.Owner = Owner;
        IKNode.SetSettingCount(1);
        IKNode.SetRootBoneName(0, "Hip");
        IKNode.SetEndBoneName(0, "Ankle");
        
        IKNode.SetTargetNode(0, IKNode.GetPathTo(targetNode));

        IKNode.SetExtendEndBone(0, false);
        
        for (int i = 0; i < IKNode.GetJointCount(0); i++)
        {
            IKNode.SetJointRotationAxis(0, i, SkeletonModifier3D.RotationAxis.X);
        }
    }

    private void AddLimbMesh(int boneIndex, Vector3 segmentOffset)
    {
        var attachment = new BoneAttachment3D
        {
            Name = GetBoneName(boneIndex),
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(attachment);
        attachment.Owner = Owner;

        var joint = new MeshInstance3D
        {
            Name = $"{GetBoneName(boneIndex)}_Joint",
            Mesh = new SphereMesh
            {
                Radius = 0.12f,
                Height = 0.24f
            }
        };

        attachment.AddChild(joint);
        joint.Owner = Owner;

        if (segmentOffset == Vector3.Zero)
        {
            return;
        }

        var segment = new MeshInstance3D
        {
            Name = "Segment",
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.15f, segmentOffset.Length(), 0.15f)
            },
            
            Position = segmentOffset / 2f,
            
            Quaternion = new Quaternion(
                Vector3.Up,
                segmentOffset.Normalized()
            )
        };
        
        attachment.AddChild(segment);
        segment.Owner = Owner;
    }
}
