using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Vector3 = Godot.Vector3;



[Tool, GlobalClass]
public partial class ProceduralSegmentedBody : Skeleton3D
{
    private struct GeneratedLimb
    {
        public LimbDef Definition;
        public int[] SegmentBones;
        public int EndBone;
        public int SoleBone;

        public GeneratedLimb(LimbDef definition, int[] segmentBones, int endBone, int soleBone)
        {
            Definition = definition;
            SegmentBones = segmentBones;
            EndBone = endBone;
            SoleBone = soleBone;
        }
    }
    
    [Export] private bool DebugMode = false;
    [ExportToolButton("Generate / Regenerate From Definition")] 
    public Callable GenerateButton => Callable.From(() => Callable.From(Generate).CallDeferred());
    [ExportToolButton("Clear")] 
    public Callable ClearButton => Callable.From(() => Callable.From(Clear).CallDeferred());
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
    private string ValidateResources()
    {
        if (BodyDefinition == null)
        {
            return "Body definition missing";
        }

        if (BodyDefinition.BodyMesh == null)
        {
            return "Body mesh is missing";
        }

        if (BodyDefinition.Limbs.Count == 0)
        {
            return "Body definition has no limbs";
        }

        HashSet<string> limbIDs = new HashSet<string>();

        for (int limbIndex = 0; limbIndex < BodyDefinition.Limbs.Count; limbIndex++)
        {
            LimbDef limb = BodyDefinition.Limbs[limbIndex];
            
            if (limb == null)
            {
                return "Initial limb resource is not set";
            }

            if (string.IsNullOrWhiteSpace(limb.LimbID))
            {
                return $"Limb {limbIndex} needs a LimbID";
            }
            
            if (!limbIDs.Add(limb.LimbID))
            {
                return $"Duplicate LimbID: {limb.LimbID}";
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

            if (limb.EndMesh != null)
            {
                float distance = limb.AnkleToSoleDistance;

                if (!float.IsFinite(distance) || distance <= 0.001f)
                {
                    return "The foot mesh sole must sit below the ankle with non-zero gap.";
                }
            }
        }

        return null;
    }
    private void Generate()
    {
        string err = ValidateResources();
        if (err != null)
        {
            GD.PrintErr(err);
            return;
        }
        
        Clear();
        

        int torsoBone = AddBone("Torso");
        SetBoneRest(torsoBone, Transform3D.Identity);

        List<GeneratedLimb> generatedLimbs = new();

        foreach (LimbDef limb in BodyDefinition.Limbs)
        {
            GeneratedLimb generatedLimb = GenerateLimbBones(limb, torsoBone);

            generatedLimbs.Add(generatedLimb);
        }
        float torsoHeight = float.PositiveInfinity;

        foreach (GeneratedLimb generatedLimb in generatedLimbs)
        {
            LimbDef limb = generatedLimb.Definition;

            if (!limb.GroundFoot)
            {
                continue;
            }

            float soleHeight = GetBoneGlobalRest(generatedLimb.SoleBone).Origin.Y;

            float requiredHeight = limb.GroundClearance - soleHeight;

            torsoHeight = Mathf.Min(torsoHeight, requiredHeight);
        }

        if (float.IsFinite(torsoHeight))
        {
            Transform3D torsoRest = GetBoneRest(torsoBone);
            torsoRest.Origin += Vector3.Up * torsoHeight;
            SetBoneRest(torsoBone, torsoRest);
        }

        ResetBonePoses();
        
        AddTorsoMesh(torsoBone);

        foreach (GeneratedLimb generatedLimb in generatedLimbs)
        {
            LimbDef limb = generatedLimb.Definition;

            for (int i = 0; i < limb.Segments.Count; i++)
            {
                SegmentDef segment = limb.Segments[i];
                
                AddSegmentMesh(
                    generatedLimb.SegmentBones[i], 
                    segment.LimbDirection * segment.Length, 
                    segment
                );
            }

            if (limb.EndMesh != null)
            {
                AddEndMesh(
                    generatedLimb.EndBone, 
                    limb.EndMesh, 
                    limb.EndMeshOffset, 
                    limb.EndMeshRotationDegrees
                );
            }
            else
            {
                AddSegmentMesh(generatedLimb.EndBone, Vector3.Zero);
            }
        }

        foreach (GeneratedLimb generatedLimb in generatedLimbs)
        {
            AddIK(
                generatedLimb.SegmentBones[0],
                generatedLimb.EndBone,
                generatedLimb.Definition
            );

            if (DebugMode)
            {
                GD.Print(
                    $"{generatedLimb.Definition.LimbID}: " +
                    $"root={generatedLimb.SegmentBones[0]}, " +
                    $"ankle={generatedLimb.EndBone}, " +
                    $"sole={generatedLimb.SoleBone}"
                );
            }
        }
    }

    private GeneratedLimb GenerateLimbBones(LimbDef limb, int torsoBone)
    {
        int parentBone = torsoBone;
        int[] segmentBones = new int[limb.Segments.Count];

        for (int i = 0; i < limb.Segments.Count; i++)
        {
            var segment = limb.Segments[i];

            int bone = AddBone($"{limb.LimbID}_segment_{i}");

            segmentBones[i] = bone;
            SetBoneParent(bone, parentBone);

            Basis rotation = Basis.FromEuler(segment.RestRotationDegrees * (Mathf.Pi / 180f));

            Vector3 position;

            if (i == 0) // first segment attaches at hip
            {
                Vector3 torsoHalfSize = BodyDefinition.TorsoSize / 2f;
                position = limb.LimbDirection * torsoHalfSize + limb.AttachmentPosition;

                Basis attachmentRotation = Basis.FromEuler(limb.AttachmentRotationDegrees * (Mathf.Pi / 180f));

                rotation = attachmentRotation * rotation;
            }
            else
            {
                SegmentDef previousSegment = limb.Segments[i - 1];
                position = previousSegment.LimbDirection * previousSegment.Length;
            }
            
            SetBoneRest(bone, new Transform3D(rotation, position));
            parentBone = bone;
        }
        
        SegmentDef lastSegment = limb.Segments[limb.Segments.Count - 1];
        
        int endBone = AddBone($"{limb.LimbID}_end");
        SetBoneParent(endBone, parentBone);

        SetBoneRest(endBone, new Transform3D(
            Basis.Identity, 
            lastSegment.LimbDirection * lastSegment.Length
        ));

        Transform3D ankleRest = GetBoneRest(endBone);
        ankleRest.Basis = GetBoneGlobalRest(parentBone).Basis.Inverse();
        SetBoneRest(endBone, ankleRest);

        int soleBone = endBone;
        
        if (limb.EndMesh != null)
        {
            Vector3 soleOffset = Vector3.Down * limb.AnkleToSoleDistance;

            soleBone = AddBone($"{limb.LimbID}_sole");
            SetBoneParent(soleBone, endBone);
            
            SetBoneRest(soleBone, new Transform3D(
                Basis.Identity,
                soleOffset
            ));
        }

        return new GeneratedLimb(limb, segmentBones, endBone, soleBone);
    }
    private void AddIK(int rootBoneIndex, int endBoneIndex, LimbDef limb)
    {
        Vector3 targetPosition = GetBoneGlobalRest(endBoneIndex).Origin;
        Vector3 rootPosition = GetBoneGlobalRest(rootBoneIndex).Origin;

        if (limb.GroundFoot)
        {
            targetPosition.Y = limb.AnkleToSoleDistance + limb.GroundClearance;
            targetPosition.Z = rootPosition.Z;
        }
        
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
            Name = $"{limb.LimbID}_CCDIK3D"
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

            if (i != 0 && i != IKNode.GetJointCount(0) - 1) // middle segments only
            {
                JointLimitationCone3D limitation = new JointLimitationCone3D();
                IKNode.SetJointLimitation(0, i, limitation);
                IKNode.SetJointLimitationRotationOffset(
                    0, 
                    i,
                    new Quaternion(0, -0.3f, 0.125f, 0) // TODO: make this based on segment variables
                );
            }
        }
    }

    private void AddTorsoMesh(int boneIndex)
    {
        var attachment = new BoneAttachment3D
        {
            Name = "Torso",
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(attachment);
        attachment.Owner = Owner;

        Mesh mesh = BodyDefinition.BodyMesh;
        
        var meshInstance = new MeshInstance3D
        {
            Name = "TorsoMesh",
            Mesh = mesh,
            Position = -mesh.GetAabb().GetCenter()    
        };
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
        meshInstance.Show();
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
        meshInstance.Show();
        joint.Show();
    }
    private void AddEndMesh(int boneIndex, Mesh mesh, Vector3 offset, Vector3 endMeshRotationDegrees)
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

        meshInstance.RotationDegrees = endMeshRotationDegrees;
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
        meshInstance.Show();
    }
}
