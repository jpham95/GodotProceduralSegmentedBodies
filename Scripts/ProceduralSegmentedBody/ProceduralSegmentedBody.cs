using System;
using System.Collections.Generic;
using System.Linq;
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
        public int ContactBone;

        public GeneratedLimb(LimbDef definition, int[] segmentBones, int endBone, int contactBone)
        {
            Definition = definition;
            SegmentBones = segmentBones;
            EndBone = endBone;
            ContactBone = contactBone;
        }
    }

    private struct JointLimitSettings
    {
        public SkeletonModifier3D.RotationAxis Axis;
        public bool Limited;
        public float ConeAngleDegrees;
        public Quaternion ConeOffset;
    }
    
    [Export] private bool _debugMode = false;
    private bool _showPreview = false;
    [Export] public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            _showPreview = value;

            if (Engine.IsEditorHint() && IsInsideTree())
            {
                UpdateGizmos();
            }
        }
    }

    [ExportToolButton("Generate / Regenerate From Definition")]
    public Callable GenerateButton => Callable.From(() => Callable.From(Generate).CallDeferred());
    [ExportToolButton("Clear")] 
    public Callable ClearButton => Callable.From(() => Callable.From(Clear).CallDeferred());
    
    [ExportGroup("Height Adjustment")]
    [Export] private float _heightFitTolerance = 0.02f;
    [Export] private float _maximumHeightAdjustment = 2f;
    [ExportToolButton("Auto Adjust Torso Height")]
    public Callable HeightAdjustButton => Callable.From(BeginHeightFit);
    
    [ExportCategory("Body Definition")]
    [Export] public BodyDef BodyDefinition = null;
    
    private ProceduralLocomotion _locomotionController;

    private bool saveRequested = false;
    private bool _busy = false;
    
    // Auto height fitting variables
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

        if (ShowPreview)
        {
            UpdateGizmos();
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
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            ClearGeneratedBody();
        }
        finally
        {
            _busy = false;
        }
    }
    private void ClearGeneratedBody()
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
    public string ValidateResources() // TODO: rewrite using LINQ
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
                float distance = limb.AnkleToContactDistance;

                if (!float.IsFinite(distance) || distance <= 0.001f)
                {
                    return "The foot mesh contact must sit below the ankle with non-zero gap.";
                }
            }
        }

        if (BodyDefinition.LocomotionSettings == null)
        {
            return "Locomotion settings resource is missing.";
        }

        return null;
    }
    
    private void SaveScene()
    {
#if TOOLS
        if (!Engine.IsEditorHint())
        {
            return;
        }

        // Error err = Error.Ok;
        Error err = EditorInterface.Singleton.SaveScene();
        ShowPreview = false;
        if (err != Error.Ok)
        {
            GD.PrintErr($"Scene could not be saved after generation: {err}");
        }
#endif
    }
    
#region Generation

    private async void Generate()
    {
        if (_busy)
        {
            return;
        }
    
        _busy = true;
        try
        {
            await GenerateAsync();
        }
        finally
        {
            _busy = false;
        }
    }
    private async System.Threading.Tasks.Task GenerateAsync()
    {
        string err = ValidateResources();
        if (err != null)
        {
            GD.PrintErr(err);
            return;
        }
        if (_debugMode)
            GD.Print($"[Generate] Before Clear: bones={GetBoneCount()}");

        SceneTree tree = GetTree();
        ClearGeneratedBody();

        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        if (_debugMode)
            GD.Print($"[Generate] After Clear: bones={GetBoneCount()}");

        _locomotionController = new ProceduralLocomotion
        {
            Name = "ProceduralLocomotion",
            settings = BodyDefinition.LocomotionSettings
        };
        AddChild(_locomotionController);
        _locomotionController.Owner = Owner;
        
        // Bones
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

            float contactHeight = GetBoneGlobalRest(generatedLimb.ContactBone).Origin.Y;

            float requiredHeight = limb.GroundClearance - contactHeight;

            torsoHeight = Mathf.Min(torsoHeight, requiredHeight);
        }

        if (float.IsFinite(torsoHeight))
        {
            Transform3D torsoRest = GetBoneRest(torsoBone);
            torsoRest.Origin += Vector3.Up * torsoHeight;
            SetBoneRest(torsoBone, torsoRest);
        }

        ResetBonePoses();
        
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        
        // Meshes
        BoneAttachment3D torsoAttachment = AddTorsoMesh(torsoBone);
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
                    segment.LengthDirection * segment.Length, 
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
        
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        
        // IK
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
                    $"end={generatedLimb.EndBone}, " +
                    $"contact={generatedLimb.ContactBone}"
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

            Basis rotation = Basis.FromEuler(Util.Vector3DegToRad(segment.RestPositionRotationDegrees));

            Vector3 position;

            if (i == 0) // first segment attaches at hip
            {
                Vector3 torsoHalfSize = BodyDefinition.TorsoSize / 2f;
                position = limb.AttachmentDirection * torsoHalfSize + limb.AttachmentPosition;

                Basis attachmentRotation = Basis.FromEuler(Util.Vector3DegToRad(limb.AttachmentRotationDegrees));

                rotation = attachmentRotation * rotation;
            }
            else
            {
                SegmentDef previousSegment = limb.Segments[i - 1];
                position = previousSegment.LengthDirection * previousSegment.Length;
            }
            
            SetBoneRest(bone, new Transform3D(rotation, position));
            parentBone = bone;
        }
        
        SegmentDef lastSegment = limb.Segments[limb.Segments.Count - 1];
        
        int endBone = AddBone($"{limb.LimbID}_end");
        SetBoneParent(endBone, parentBone);

        SetBoneRest(endBone, new Transform3D(
            Basis.Identity, 
            lastSegment.LengthDirection * lastSegment.Length
        ));

        if (limb.GroundFoot)
        {
            Transform3D endRest = GetBoneRest(endBone);
            endRest.Basis = GetBoneGlobalRest(parentBone).Basis.Inverse();
            SetBoneRest(endBone, endRest);
        }

        int contactBone = endBone;
        
        if (limb.GroundFoot && limb.EndMesh != null)
        {
            Vector3 contactOffset = Vector3.Down * limb.AnkleToContactDistance;

            contactBone = AddBone($"{limb.LimbID}_contact");
            SetBoneParent(contactBone, endBone);
            
            SetBoneRest(contactBone, new Transform3D(
                Basis.Identity,
                contactOffset
            ));
        }

        return new GeneratedLimb(limb, segmentBones, endBone, contactBone);
    }

    private JointLimitSettings GetJointLimitSettings(SegmentDef segment)
    {
        JointLimitSettings settings = new()
        {
            Axis = SkeletonModifier3D.RotationAxis.All,
            Limited = false,
            ConeAngleDegrees = 90f,
            ConeOffset = Quaternion.Identity
        };
        
        switch (segment.JointPreset)
        {
            case SegmentDef.JointPresetEnum.Hip:
                break;
            case SegmentDef.JointPresetEnum.Knee:
                settings.Axis = SkeletonModifier3D.RotationAxis.X;
                settings.Limited = true;
                settings.ConeOffset = new Quaternion(0f, -0.3f, 0.125f, 0f).Normalized();
                break;
            case SegmentDef.JointPresetEnum.Free:
                break;
            case SegmentDef.JointPresetEnum.Custom:
                settings.Axis = segment.JointRotationAxis;
                settings.Limited = segment.LimitJoint;
                settings.ConeAngleDegrees = segment.JointConeAngleDegrees;

                settings.ConeOffset = Basis.FromEuler(
                    Util.Vector3DegToRad(segment.JointLimitRotationDegrees)
                ).GetRotationQuaternion();
                break;
            default:
                GD.PrintErr($"{segment.GetName()} has invalid JointPreset");
                break;
        }

        return settings;
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
            targetPosition.Y = limb.AnkleToContactDistance + limb.GroundClearance;
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
        
        void ConfigureIK(int setting, int endBone, Marker3D target)
        {
            IKNode.SetRootBoneName(setting, GetBoneName(rootBoneIndex));
            IKNode.SetEndBoneName(setting, GetBoneName(endBone));
            IKNode.SetExtendEndBone(setting, false);
            IKNode.SetTargetNode(setting, IKNode.GetPathTo(target));
        }
        
        AddChild(IKNode);
        
        IKNode.Owner = Owner;

        bool useMagnet = limb.UseMagnetTargets && segmentBones.Length > 2;
        int endpointSetting = useMagnet ? 1 : 0;
        
        IKNode.SetSettingCount(useMagnet ? 2 : 1);

        if (useMagnet)
        {
            int magnetBone = segmentBones[segmentBones.Length / 2 + 1];

            Vector3 magnetRestPosition = GetBoneGlobalRest(magnetBone).Origin;

            Marker3D magnet = new Marker3D
            {
                Name = $"{limb.LimbID}_MagnetNode",
                Position = targetNode.Transform.AffineInverse() * magnetRestPosition
            };

            targetNode.AddChild(magnet);
            magnet.Owner = Owner;

            ConfigureIK(0, magnetBone, magnet);
        }

        ConfigureIK(endpointSetting, endBoneIndex, targetNode);

        for (int setting = 0; setting < IKNode.GetSettingCount(); setting++)
        {
            for (int jointIndex = 0; jointIndex < IKNode.GetJointCount(setting); jointIndex++)
            {
                int boneIndex = IKNode.GetJointBone(setting, jointIndex);
                int segmentIndex = System.Array.IndexOf(segmentBones, boneIndex);

                if (segmentIndex < 0)
                {
                    continue;
                }

                SegmentDef segment = limb.Segments[segmentIndex];

                JointLimitSettings settings = GetJointLimitSettings(segment);

                IKNode.SetJointRotationAxis(
                    setting,
                    jointIndex,
                    settings.Axis
                );

                if (!settings.Limited)
                {
                    continue;
                }

                IKNode.SetJointLimitation(setting, jointIndex, new JointLimitationCone3D
                {
                    Angle = Mathf.DegToRad(settings.ConeAngleDegrees)
                });

                IKNode.SetJointLimitationRotationOffset(
                    setting,
                    jointIndex,
                    settings.ConeOffset
                );
            }
        }

        if (limb.AlignFootToGround && limb.EndMesh != null)
        {
            AddFootOrientation(endBoneIndex, targetNode, limb);
        }
        
        _locomotionController.AddLimb(limb, targetNode, targetPosition);
    }

    private BoneAttachment3D AddTorsoMesh(int boneIndex)
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
        
        MeshDebug(meshInstance);
        
        meshInstance.Show();
        return attachment;
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
        
        MeshDebug(meshInstance);
        
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
        
        MeshDebug(meshInstance);
        
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
#endregion
    
#region Height Fitting
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
#endregion

    // TODO: preview for IK markers, joint limitations?, feet, joints
    public Vector3[] GetPreview(out List<Vector3> jointPositions)
    {
        jointPositions = new();
        List<Vector3> previewLines = new();
        
        if (BodyDefinition?.TorsoMesh == null)
        {
            return [];
        }
        
        // helpers
        void AddLine(Vector3 start, Vector3 end)
        {
            previewLines.Add(start);
            previewLines.Add(end);
        }

        void AddBox(Aabb bounds, Transform3D transform)
        {
            Vector3 min = bounds.Position;
            Vector3 max = bounds.Position + bounds.Size;

            Vector3[] corners =
            {
                new(min.X, min.Y, min.Z),
                new(max.X, min.Y, min.Z),
                new(max.X, min.Y, max.Z),
                new(min.X, min.Y, max.Z),

                new(min.X, max.Y, min.Z),
                new(max.X, max.Y, min.Z),
                new(max.X, max.Y, max.Z),
                new(min.X, max.Y, max.Z),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = transform * corners[i];
            }

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                
                AddLine(corners[i], corners[next]);
                AddLine(corners[i + 4], corners[next + 4]);
                AddLine(corners[i], corners[i + 4]);
            }
        }

        void AddLimitationPreview(Transform3D jointTransform, SegmentDef segment)
        {
            JointLimitSettings settings = GetJointLimitSettings(segment);

            float radius = segment.Length * 0.3f;

            Vector3 localAxis = settings.Axis switch
            {
                SkeletonModifier3D.RotationAxis.X => Vector3.Right,
                SkeletonModifier3D.RotationAxis.Y => Vector3.Up,
                SkeletonModifier3D.RotationAxis.Z => Vector3.Back,
                SkeletonModifier3D.RotationAxis.Custom => Vector3.Right,
                _ => Vector3.Zero,
            };

            if (localAxis != Vector3.Zero)
            {
                Vector3 axis = jointTransform.Basis * localAxis;
                
                AddLine(
                    jointTransform.Origin - axis * radius,
                    jointTransform.Origin + axis * radius
                );
            }

            if (!settings.Limited)
            {
                return;
            }

            float fullAngle = Mathf.Clamp(settings.ConeAngleDegrees, 0f, 360f);

            if (fullAngle == 360f)
            {
                return;
            }

            Quaternion directionRotation = new Quaternion(
                Vector3.Up,
                segment.LengthDirection.Normalized()
            );

            Basis coneBasis = jointTransform.Basis * new Basis(directionRotation) * new Basis(settings.ConeOffset);

            Transform3D coneTransform = new Transform3D(
                coneBasis,
                jointTransform.Origin
            );

            // https://github.com/godotengine/godot/blob/4.6/scene/resources/3d/joint_limitation_cone_3d.cpp
            
            float halfAngle = Mathf.DegToRad(fullAngle) * 0.5f;
            float ringRadius = Mathf.Sin(halfAngle) * radius;
            float ringHeight = Mathf.Cos(halfAngle) * radius;

            const int ringSegments = 32;

            for (int i = 0; i < ringSegments; i++)
            {
                float angleA = Mathf.Tau * i / ringSegments;
                float angleB = Mathf.Tau * (i + 1) / ringSegments;

                Vector3 pointA = new Vector3(
                    Mathf.Cos(angleA) * ringRadius,
                    ringHeight,
                    Mathf.Sin(angleA) * ringRadius
                );
                
                Vector3 pointB = new Vector3(
                    Mathf.Cos(angleB) * ringRadius,
                    ringHeight,
                    Mathf.Sin(angleB) * ringRadius
                );
                
                AddLine(coneTransform * pointA, coneTransform * pointB);

                if (i % (ringSegments / 8) == 0)
                {
                    AddLine(jointTransform.Origin, coneTransform * pointA);
                }
            }
        }
        
        // centered torso mesh
        Vector3 halfTorsoSize = BodyDefinition.TorsoSize / 2f;

        Vector3[] extentVertices =
        {
            // bottom face
            new(-halfTorsoSize.X, -halfTorsoSize.Y, -halfTorsoSize.Z),
            new(halfTorsoSize.X, -halfTorsoSize.Y, -halfTorsoSize.Z),
            new(halfTorsoSize.X, -halfTorsoSize.Y, halfTorsoSize.Z),
            new(-halfTorsoSize.X, -halfTorsoSize.Y, halfTorsoSize.Z),
            
            // top face
            new(-halfTorsoSize.X, halfTorsoSize.Y, halfTorsoSize.Z),
            new(-halfTorsoSize.X, halfTorsoSize.Y, -halfTorsoSize.Z),
            new(halfTorsoSize.X, halfTorsoSize.Y, -halfTorsoSize.Z),
            new(halfTorsoSize.X, halfTorsoSize.Y, halfTorsoSize.Z),
        };

        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;

            AddLine(extentVertices[i], extentVertices[next]);
            AddLine(extentVertices[i + 4], extentVertices[next + 4]);
            AddLine(extentVertices[i], extentVertices[next + 4]);
        }

        float torsoHeight = float.PositiveInfinity;

        // head
        if (BodyDefinition.HeadMesh != null)
        {
            Aabb headBounds = BodyDefinition.HeadMesh.GetAabb();

            Basis headRotation = Basis.FromEuler(Util.Vector3DegToRad(BodyDefinition.HeadRotationDegrees));
            
            Aabb rotatedBounds = new Transform3D(headRotation, Vector3.Zero) * headBounds;

            Vector3 meshOffset = new Vector3(
                -rotatedBounds.GetCenter().X,
                -rotatedBounds.Position.Y,
                -rotatedBounds.GetCenter().Z
            );

            Vector3 headPosition = Vector3.Up * (BodyDefinition.TorsoSize.Y / 2f) + BodyDefinition.HeadAttachmentOffset;

            Transform3D headTransform = new Transform3D(headRotation, headPosition + meshOffset);

            Vector3 min = headBounds.Position;
            Vector3 max = headBounds.Position + headBounds.Size;

            Vector3[] headCorners =
            {
                new(min.X, min.Y, min.Z),
                new(max.X, min.Y, min.Z),
                new(max.X, min.Y, max.Z),
                new(min.X, min.Y, max.Z),

                new(min.X, max.Y, max.Z),
                new(min.X, max.Y, min.Z),
                new(max.X, max.Y, min.Z),
                new(max.X, max.Y, max.Z),
            };

            for (int i = 0; i < headCorners.Length; i++)
            {
                headCorners[i] = headTransform * headCorners[i];
            }

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                
                AddLine(headCorners[i], headCorners[next]);
                AddLine(headCorners[i + 4], headCorners[next + 4]);
                AddLine(headCorners[i], headCorners[next + 4]);
            }
        }
        
        // limbs
        foreach (LimbDef limb in BodyDefinition.Limbs)
        {
            if (limb == null || limb.Segments.Count == 0)
            {
                continue;
            }

            bool validLimb = !limb.Segments.Any(segment => 
                segment == null || 
                segment.Mesh == null || 
                !float.IsFinite(segment.Length) || 
                segment.Length <= 0f);

            if (!validLimb)
            {
                continue;
            }

            Transform3D boneTransform = Transform3D.Identity;
            Vector3 anklePosition = Vector3.Zero;
            Vector3 rootPosition = Vector3.Zero;

            for (int i = 0; i < limb.Segments.Count; i++)
            {
                SegmentDef segment = limb.Segments[i];

                Basis rotation = Basis.FromEuler(Util.Vector3DegToRad(segment.RestPositionRotationDegrees));

                Vector3 position;

                if (i == 0)
                {
                    position = limb.AttachmentDirection * halfTorsoSize + limb.AttachmentPosition;

                    Basis attachmentRotation = Basis.FromEuler(Util.Vector3DegToRad(limb.AttachmentRotationDegrees));

                    rotation = attachmentRotation * rotation;
                }
                else
                {
                    SegmentDef previous = limb.Segments[i - 1];
                    position = previous.LengthDirection * previous.Length;
                }

                boneTransform *= new Transform3D(rotation, position);
                
                AddLimitationPreview(boneTransform, segment);

                if (i == 0)
                {
                    rootPosition = boneTransform.Origin;
                }
                
                jointPositions.Add(boneTransform.Origin);
                
                Vector3 segmentEnd = boneTransform * (segment.LengthDirection * segment.Length);
                
                AddLine(boneTransform.Origin, segmentEnd);
                anklePosition = segmentEnd;
            }
            
            jointPositions.Add(anklePosition);
            
            Vector3 contactPosition = anklePosition;

            if (limb.EndMesh != null)
            {
                contactPosition += Vector3.Down * limb.AnkleToContactDistance;
                AddLine(anklePosition, contactPosition);

                Basis footRotation = Basis.FromEuler(
                    Util.Vector3DegToRad(limb.EndMeshRotationDegrees)
                );

                Basis endBasis = limb.GroundFoot
                    ? Basis.Identity
                    : boneTransform.Basis;
                
                Transform3D footTransform = new Transform3D(endBasis * footRotation, anklePosition + endBasis * limb.EndMeshOffset);
                
                AddBox(limb.EndMesh.GetAabb(), footTransform);
            }

            if (limb.GroundFoot)
            {
                torsoHeight = Mathf.Min(torsoHeight, limb.GroundClearance - contactPosition.Y);
            }
        }

        Vector3 groundedOffset = float.IsFinite(torsoHeight)
            ? Vector3.Up * torsoHeight
            : Vector3.Zero;

        for (int i = 0; i < previewLines.Count; i++)
        {
            previewLines[i] += groundedOffset;
        }

        for (int i = 0; i < jointPositions.Count; i++)
        {
            jointPositions[i] += groundedOffset;
        }

        return previewLines.ToArray();
    }

    private void MeshDebug(MeshInstance3D meshInstance)
    {
        // if (_debugMode)
        // {
        //     string path = meshInstance.GetPath().ToString();
        //     ulong id = meshInstance.GetInstanceId();
        //     string name = path.Split("/")[^2];
        //     
        //     GD.Print($"[Mesh created] {name} id={id}");
        //     meshInstance.VisibilityChanged += () =>
        //     {
        //         if (!meshInstance.Visible)
        //         {
        //             GD.Print($"[Mesh hidden] {name} id={id}");
        //         }
        //     };
        //     meshInstance.TreeExiting += () =>
        //         GD.Print($"[Mesh exiting] {name} id={id}");
        // }
    }
}
