using Godot;
using Vector3 = Godot.Vector3;

[Tool]
public partial class TestLimb : Skeleton3D
{
    [ExportToolButton("Generate / Regenerate From Definition")] 
    public Callable CreateLimbFromDefinitionButton => Callable.From(() => Callable.From(CreateLimbFromDefinition).CallDeferred());
    [ExportToolButton("Clear")] 
    public Callable ClearButton => Callable.From(Clear);

    [Export] public BodyDef BodyDefinition = null;

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        ClearBones();
    }

    private void CreateLimbFromDefinition()
    {
        string err = ValidateResources();
        if (err != null)
        {
            GD.PrintErr(err);
            return;
        }
        
        Clear();
        
        var limb = BodyDefinition.Limbs[0];
        int parentBone = -1;

        for (int i = 0; i < limb.Segments.Count; i++)
        {
            var segment = limb.Segments[i];

            int bone = AddBone($"{limb.LimbID}_segment_{i}");

            Basis rotation = Basis.FromEuler(segment.RestRotationDegrees * (Mathf.Pi / 180f));

            Vector3 position;

            if (i == 0) // first segment attaches at hip
            {
                position = limb.AttachmentPosition;

                Basis attachmentRotation = Basis.FromEuler(limb.AttachmentRotationDegrees * (Mathf.Pi / 180f));

                rotation = attachmentRotation * rotation;
            }
            else
            {
                SetBoneParent(bone, parentBone);
                SegmentDef previousSegment = limb.Segments[i - 1];
                position = previousSegment.LimbDirection * previousSegment.Length;
            }
            
            SetBoneRest(bone, new Transform3D(rotation, position));
            parentBone = bone;
        }
        
        SegmentDef lastSegment = limb.Segments[limb.Segments.Count - 1];
        
        int endBone = AddBone($"{limb.LimbID}_end");
        SetBoneParent(endBone, parentBone);

        SetBoneRest(endBone, new Transform3D(Basis.Identity, lastSegment.LimbDirection * lastSegment.Length));

        float endHeight = GetBoneGlobalRest(endBone).Origin.Y;

        Transform3D rootRest = GetBoneRest(0);
        rootRest.Origin += Vector3.Up * -endHeight + new Vector3(0, limb.EndMeshHeight, 0);
        SetBoneRest(0, rootRest);
        ResetBonePoses();

        for (int i = 0; i < limb.Segments.Count; i++)
        {
            SegmentDef segment = limb.Segments[i];
            AddSegmentMesh(i, segment.LimbDirection * segment.Length, segment);
        }

        if (limb.EndMesh != null)
        {
            AddEndMesh(endBone, limb.EndMesh, limb.EndMeshOffset);
        }
        else
        {
            AddSegmentMesh(endBone, Vector3.Zero);
        }
        
        AddIK(0, endBone, limb);
    }

    private string ValidateResources()
    {
        if (BodyDefinition == null)
        {
            return "Body definition missing";
        }

        if (BodyDefinition.Limbs.Count == 0)
        {
            return "Body definition has no limbs";
        }

        var limb = BodyDefinition.Limbs[0];

        if (limb == null)
        {
            return "Initial limb resource is not set";
        }
        
        if (limb.Segments.Count == 0)
        {
            return "Initial limb has no segments";
        }

        for (int i = 0; i < limb.Segments.Count; i++)
        {
            SegmentDef segment = limb.Segments[i];
            if (segment == null)
            {
                return $"Segment {i} is null";
            }

            if (segment.Mesh == null)
            {
                return $"Segment {i} has no mesh";
            }
            
            if (segment.Length <= 0f)
            {
                return $"Segment {i} must have a positive length";
            }
        }

        return null;
    }
    private void AddIK(int rootBoneIndex, int endBoneIndex, LimbDef limb)
    {
        Vector3 targetPosition = GetBoneGlobalRest(endBoneIndex).Origin;
        Vector3 rootPosition = GetBoneGlobalRest(rootBoneIndex).Origin;

        targetPosition.Y = 0f;
        targetPosition.Z = rootPosition.Z;
        
        Marker3D targetNode = new Marker3D
        {
            Name = $"{limb.LimbID}_TargetNode",
            Position = targetPosition
        };
        
        AddChild(targetNode);
        targetNode.Owner = Owner;

        // Fabrik3D IKNode = new Fabrik3D
        // {
        //     Name = "FABRIK3D"
        // };

        Ccdik3D IKNode = new Ccdik3D
        {
            Name = "CCDIK3D"
        };
        
        AddChild(IKNode);
        
        IKNode.Owner = Owner;
        
        IKNode.SetSettingCount(1);
        IKNode.SetRootBoneName(0, GetBoneName(rootBoneIndex));
        IKNode.SetEndBoneName(0, GetBoneName(endBoneIndex));
        
        IKNode.SetExtendEndBone(0, false);
        
        IKNode.SetTargetNode(0, IKNode.GetPathTo(targetNode));
        
        for (int i = 0; i < IKNode.GetJointCount(0); i++)
        {
            if (limb.LockIKToAxis)
            {
                IKNode.SetJointRotationAxis(0, i, limb.IKLockAxis);
            }

            if (i != 0 && i != IKNode.GetJointCount(0) - 1)
            {
                JointLimitationCone3D limitation = new JointLimitationCone3D();
                IKNode.SetJointLimitation(0, i, limitation);
                IKNode.SetJointLimitationRotationOffset(0, i, new Quaternion(0, -0.3f, 0.125f, 0));
            }
        }
        
        
        AddFootOrientation(endBoneIndex, targetNode);
    }
    private void AddSegmentMesh(int boneIndex, Vector3 segmentOffset, SegmentDef segmentDef = null)
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
            Mesh = this.BodyDefinition.JointMesh ?? new SphereMesh{ Radius = 0.12f, Height = 0.24f }
        };

        attachment.AddChild(joint);
        joint.Owner = Owner;

        if (segmentOffset == Vector3.Zero)
        {
            return;
        }

        if (segmentDef?.Mesh == null)
        {
            GD.PrintErr("Segment mesh is null");
            return;
        }

        
        
        MeshInstance3D meshInstance = new MeshInstance3D { Name = "SegmentMesh" };
        meshInstance.Mesh = segmentDef.Mesh;
        meshInstance.RotationDegrees = segmentDef.MeshRotationDegrees;
        meshInstance.Scale = segmentDef.Scale;
        
        // account for mesh origin, rotation + scale
        Vector3 meshCenter = segmentDef.Mesh.GetAabb().GetCenter();
        Vector3 transformedCenter = meshInstance.Basis * meshCenter;
        
        meshInstance.Position = segmentOffset / 2f - transformedCenter + segmentDef.MeshOffset;
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
    }

    private void AddEndMesh(int boneIndex, Mesh mesh, Vector3 offset)
    {
        BoneAttachment3D attachment = new BoneAttachment3D
        {
            Name = GetBoneName(boneIndex),
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(attachment);
        attachment.Owner = Owner;

        MeshInstance3D meshInstance = new MeshInstance3D
        {
            Name = "EndMesh",
            Mesh = mesh,
            Position = offset
        };
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
    }

    private void AddFootOrientation(int endBoneIndex, Node3D targetNode)
    {
        CopyTransformModifier3D footOrientation = new CopyTransformModifier3D
        {
            Name = "FootOrientation"
        };
        
        AddChild(footOrientation);
        footOrientation.Owner = Owner;
        
        footOrientation.SetSettingCount(1);
        footOrientation.SetApplyBoneName(0, GetBoneName(endBoneIndex));
        
        footOrientation.SetReferenceType(0, BoneConstraint3D.ReferenceType.Node);
        
        footOrientation.SetReferenceNode(0, footOrientation.GetPathTo(targetNode));
        
        footOrientation.SetCopyPosition(0, false);
        footOrientation.SetCopyRotation(0, true);
        footOrientation.SetCopyScale(0, false);
        
        footOrientation.SetRelative(0, false);
        footOrientation.SetAdditive(0, false);
        footOrientation.SetAmount(0, 1f);
        
        footOrientation.SetAxisXEnabled(0, true);
        footOrientation.SetAxisYEnabled(0, true);
        footOrientation.SetAxisZEnabled(0, true);
    }
}
