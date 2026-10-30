using System;
using System.Collections.Generic;
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
    
    [Export] private bool _debugMode = false;
    [ExportToolButton("Generate / Regenerate From Definition")] 
    public Callable GenerateButton => Callable.From(() => Callable.From(Generate).CallDeferred());
    [ExportToolButton("Clear")] 
    public Callable ClearButton => Callable.From(() => Callable.From(Clear).CallDeferred());
    
    [Export] public LocomotionSettings LocomotionSettings;
    
    [ExportGroup("Height Adjustment")]
    [Export] private float _heightFitTolerance = 0.02f;
    [Export] private float _maximumHeightAdjustment = 2f;
    [ExportToolButton("Auto Adjust Torso Height")]
    public Callable HeightAdjustButton => Callable.From(BeginHeightFit);

    
    [ExportCategory("Default Step Curves")]
    [Export] public Curve DefaultStepProgressCurve;
    [Export] public Curve DefaultStepLiftCurve;
    [ExportCategory("Body Definition")]
    [Export] public BodyDef BodyDefinition = null;
    
    private ProceduralLocomotion _locomotionController;

    private bool saveRequested = false;
    
    // Auto height fitting
    private List<(int Bone, Marker3D target)> heightFitFeet = new();
    private bool heightSampleReady = false;
    private bool fittingHeight = false;
    private int fitTorsoBone = -1;
    private int fitSettleUpdates;
    private int fitStalledAttempts;
    private int fitAttempts;
    
    private float fitStartingHeight;
    private float fitBestHeight;
    private float fitBestError;
    private float fitVerticalError;
    private float fitReachError;
    
    private const int FitSettleUpdates = 12;
    private const int FitMaximumAttempts = 40;
    public override void _Process(double delta)
    {
#if TOOLS
        if (!Engine.IsEditorHint())
        {
            return;
        }

        if (fittingHeight)
        {
            if (heightSampleReady)
            {
                ApplyHeightFit();
            }

            return;
        }

        if (saveRequested)
        {
            saveRequested = false;
            SaveScene();
        }
#endif
    }

    private void Clear()
    {
        DisconnectHeightFit();
        fittingHeight = false;
        heightSampleReady = false;
        saveRequested = false;
        
        heightFitFeet.Clear();
        
        foreach (Node child in GetChildren())
        {
            if (child is SkeletonModifier3D modifier)
            {
                modifier.Active = false;
            }

            if (child is IKModifier3D ik)
            {
                ik.ClearSettings();
            }

            if (child is CopyTransformModifier3D footOrientation)
            {
                footOrientation.SetSettingCount(0);
            }
        }

        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        ClearBones();
        _locomotionController = null;
    }
    public string ValidateResources()
    {
        if (BodyDefinition == null)
        {
            return "Body definition missing";
        }

        if (BodyDefinition.TorsoMesh == null)
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

        if (LocomotionSettings == null)
        {
            return "Locomotion settings resource is missing.";
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
        if (_debugMode)
            GD.Print($"[Generate] Before Clear: bones={GetBoneCount()}");
        
        Clear();
        
        if (_debugMode)
            GD.Print($"[Generate] After Clear: bones={GetBoneCount()}");

        _locomotionController = new ProceduralLocomotion
        {
            Name = "ProceduralLocomotion",
            settings = LocomotionSettings
        };
        AddChild(_locomotionController);
        _locomotionController.Owner = Owner;
        
        int torsoBone = AddBone("Torso");
        SetBoneRest(torsoBone, Transform3D.Identity);

        int headBone = -1;

        if (BodyDefinition.HeadMesh != null)
        {
            headBone = AddBone("Head");
            SetBoneParent(headBone, torsoBone);

            Vector3 headPosition = Vector3.Up * (BodyDefinition.TorsoSize.Y / 2f) + BodyDefinition.HeadAttachmentOffset;
            
            SetBoneRest(
                headBone,
                new Transform3D(Basis.Identity, headPosition)
            );
        }

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
        if (headBone >= 0)
        {
            AddHeadMesh(headBone);
        }

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
                generatedLimb.SegmentBones,
                generatedLimb.EndBone,
                generatedLimb.Definition
            );

            if (_debugMode)
            {
                GD.Print(
                    $"{generatedLimb.Definition.LimbID}: " +
                    $"root={generatedLimb.SegmentBones[0]}, " +
                    $"ankle={generatedLimb.EndBone}, " +
                    $"sole={generatedLimb.SoleBone}"
                );
            }
        }
        
        if (_debugMode) GD.Print($"[Generate] All bones created: bones={GetBoneCount()}");
        if (Engine.IsEditorHint())
        {
            fitTorsoBone = torsoBone;
            saveRequested = true;
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
    private void AddIK(int[] segmentBones, int endBoneIndex, LimbDef limb)
    {
        int rootBoneIndex = segmentBones[0];
        
        if (_debugMode)
            GD.Print(
                $"[AddIK] skeleton={GetPath()}, " +
                $"limb={limb.LimbID}, count={GetBoneCount()}, " +
                $"root={rootBoneIndex}, end={endBoneIndex}"
            );
        
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

        if (limb.GroundFoot)
        {
            heightFitFeet.Add((endBoneIndex, targetNode));
        }

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
        
        for (int jointIndex = 0; jointIndex < IKNode.GetJointCount(0); jointIndex++)
        {
            int boneIndex = IKNode.GetJointBone(0, jointIndex);
            int segmentIndex = System.Array.IndexOf(segmentBones, boneIndex);

            if (segmentIndex < 0)
            {
                continue;
            }

            SegmentDef segment = limb.Segments[segmentIndex];
            
            SkeletonModifier3D.RotationAxis axis = SkeletonModifier3D.RotationAxis.All;
            bool limitJoint = false;
            float coneAngleDegrees = 90f;
            Quaternion coneOffset = Quaternion.Identity;

            switch (segment.JointPreset)
            {
                case SegmentDef.JointPresetEnum.Hip:
                    break;
                case SegmentDef.JointPresetEnum.Knee:
                    axis = SkeletonModifier3D.RotationAxis.X;
                    limitJoint = true;
                    coneOffset = new Quaternion(0f, -0.3f, 0.125f, 0f).Normalized();
                    break;
                case SegmentDef.JointPresetEnum.Free:
                    break;
                case SegmentDef.JointPresetEnum.Custom:
                    axis = segment.JointRotationAxis;
                    limitJoint = segment.LimitJoint;
                    coneAngleDegrees = segment.JointConeAngleDegrees;

                    coneOffset = Basis.FromEuler(
                        segment.JointLimitRotationDegrees * (Mathf.Pi / 180f)
                    ).GetRotationQuaternion();
                    break;
                default:
                    GD.PrintErr($"{segment.GetName()} has invalid JointPreset");
                    continue;
            }
            
            IKNode.SetJointRotationAxis(
                0,
                jointIndex,
                axis
            );

            
            if (!limitJoint)
            {
                continue;
            }
            
            IKNode.SetJointLimitation(0, jointIndex, new JointLimitationCone3D
            {
                Angle = Mathf.DegToRad(coneAngleDegrees)
            });

            IKNode.SetJointLimitationRotationOffset(
                0,
                jointIndex,
                coneOffset
            );
        }

        if (limb.GroundFoot && limb.EndMesh != null)
        {
            AddFootOrientation(endBoneIndex, targetNode, limb);
        }
        
        _locomotionController.AddLimb(limb, targetNode, targetPosition);
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

        Mesh mesh = BodyDefinition.TorsoMesh;
        
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

    private void AddHeadMesh(int boneIndex)
    {
        var headAttachment = new BoneAttachment3D
        {
            Name = "Head",
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(headAttachment);
        headAttachment.Owner = Owner;

        MeshInstance3D meshInstance = new MeshInstance3D
        {
            Name = "HeadMesh",
            Mesh = BodyDefinition.HeadMesh,
            RotationDegrees = BodyDefinition.HeadRotationDegrees
        };

        Aabb bounds = new Transform3D(meshInstance.Basis, Vector3.Zero) * BodyDefinition.HeadMesh.GetAabb();

        meshInstance.Position = new Vector3(
            -bounds.GetCenter().X,
            -bounds.Position.Y,
            -bounds.GetCenter().Z
        );
        
        headAttachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
    }

    private void AddFootOrientation(int ankleBoneIndex, Marker3D target, LimbDef limb)
    {
        var modifier = new CopyTransformModifier3D
        {
            Name = $"{limb.LimbID}_FootOrientation"
        };
        
        AddChild(modifier);
        modifier.Owner = Owner;

        modifier.SetSettingCount(1);
        modifier.SetApplyBoneName(0, GetBoneName(ankleBoneIndex));
        
        modifier.SetReferenceType(0, BoneConstraint3D.ReferenceType.Node);
        modifier.SetReferenceNode(0, modifier.GetPathTo(target));
        
        modifier.SetCopyPosition(0, false);
        modifier.SetCopyRotation(0, true);
        modifier.SetCopyScale(0, false);
        
        modifier.SetRelative(0, false);
        modifier.SetAdditive(0, false);
        modifier.SetAmount(0, 1f);
        
        modifier.SetAxisXEnabled(0, true);
        modifier.SetAxisYEnabled(0, true);
        modifier.SetAxisZEnabled(0, true);
    }

    private void SaveScene()
    {
#if TOOLS
        if (!Engine.IsEditorHint())
        {
            return;
        }

        Error err = EditorInterface.Singleton.SaveScene();

        if (err != Error.Ok)
        {
            GD.PrintErr($"Scene could not be saved after generation: {err}");
        }
#endif
    }

    private void DisconnectHeightFit()
    {
        Callable heightMeasureCallback = Callable.From(MeasureHeightFit);

        if (IsConnected(Skeleton3D.SignalName.SkeletonUpdated, heightMeasureCallback))
        {
            Disconnect(Skeleton3D.SignalName.SkeletonUpdated, heightMeasureCallback);
        }
    }
    
    private void MeasureHeightFit()
    {
        if (!fittingHeight || heightSampleReady)
        {
            return;
        }
        
        fitSettleUpdates++;

        if (fitSettleUpdates < FitSettleUpdates)
        {
            return;
        }

        fitVerticalError = float.NegativeInfinity;
        fitReachError = 0f;

        foreach (var foot in heightFitFeet)
        {
            Vector3 actual = GetBoneGlobalPose(foot.Bone).Origin;
            Vector3 preferred = ToLocal(foot.target.GlobalPosition);

            // find lowest foot point
            fitVerticalError = Mathf.Max(
                fitVerticalError,
                preferred.Y - actual.Y
            );

            // full distance
            fitReachError = Mathf.Max(
                fitReachError,
                actual.DistanceTo(preferred)
            );
        }

        heightSampleReady = true;
    }

    private void ApplyHeightFit()
    {
        heightSampleReady = false;

        if (!float.IsFinite(fitVerticalError) || !float.IsFinite(fitReachError))
        {
            FinishHeightFit(false);
            return;
        }

        float currentHeight = GetBoneRest(fitTorsoBone).Origin.Y;

        if (fitReachError < fitBestError)
        {
            bool improved = fitBestError - fitReachError > 0.0001f;

            fitBestError = fitReachError;
            fitBestHeight = currentHeight;

            if (improved)
            {
                fitStalledAttempts = 0;
            }
            else
            {
                fitStalledAttempts++;
            }
        }
        else
        {
            fitStalledAttempts++;
        }

        float tolerance = Mathf.Max(_heightFitTolerance, 0.001f);

        if (_debugMode)
        {
            GD.Print(
                $"[HeightFit] height={currentHeight:F3}, " +
                $"veritcalError={fitVerticalError:F3}, " +
                $"reachError={fitReachError:F3}"
            );
        }

        if (fitReachError <= tolerance)
        {
            FinishHeightFit(true);
            return;
        }

        if (fitAttempts >= FitMaximumAttempts ||
            fitStalledAttempts >= 5)
        {
            FinishHeightFit(false);
            return;
        }

        float correction = Mathf.Clamp(
            fitVerticalError * 0.5f,
            -0.1f,
            0.1f
        );

        float maximumAdjustment = Mathf.Max(_maximumHeightAdjustment, 0f);

        float nextHeight = Mathf.Clamp(
            currentHeight + correction,
            fitStartingHeight - maximumAdjustment,
            fitStartingHeight + maximumAdjustment
        );

        if (Mathf.Abs(nextHeight - currentHeight) < 0.0001f)
        {
            FinishHeightFit(false);
            return;
        }

        SetFittedTorsoHeight(nextHeight);

        fitAttempts++;
        fitSettleUpdates = 0;
    }
    private void BeginHeightFit()
    {
        if (!Engine.IsEditorHint() || fittingHeight)
        {
            return;
        }

        if (fitTorsoBone == -1)
        {
            GD.PrintErr("Generate body before adjusting height");
            return;
        }
        
        fitStartingHeight = GetBoneRest(fitTorsoBone).Origin.Y;
        fitBestHeight = fitStartingHeight;
        fitBestError = float.PositiveInfinity;

        fitAttempts = 0;
        fitStalledAttempts = 0;
        fitSettleUpdates = 0;

        heightSampleReady = false;
        fittingHeight = true;

        DisconnectHeightFit();
        SkeletonUpdated += MeasureHeightFit;
    }

    private void SetFittedTorsoHeight(float height)
    {
        Transform3D rest = GetBoneRest(fitTorsoBone);
        rest.Origin.Y = height;
        
        SetBoneRest(fitTorsoBone, rest);
        ResetBonePose(fitTorsoBone);
    }

    private void FinishHeightFit(bool reachedTargets)
    {
        DisconnectHeightFit();

        fittingHeight = false;
        heightSampleReady = false;

        if (!reachedTargets)
        {
            SetFittedTorsoHeight(fitBestHeight);

            GD.Print(
                "Height fitting could not reach every foot target.\nJoint limits or segment angles may need adjusting.");
        }

        saveRequested = true;
    }
}
