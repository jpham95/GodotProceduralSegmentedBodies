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
    
    [ExportCategory("Body Definition")]
    [Export] public BodyDef BodyDefinition = null;
    
    private ProceduralLocomotion _locomotionController;

    private bool _saveRequested = false;
    private bool _busy = false;
    
    // Auto height fitting variables
    private float _heightFitTolerance = 0.02f;
    private float _maximumHeightAdjustment = 2f;
    private List<(int Bone, Marker3D target)> _heightFitFeet = new();
    private bool _heightSampleReady = false;
    private bool _fittingHeight = false;
    private int _fitTorsoBone = -1;
    private int _fitSettleUpdates;
    private int _fitStalledAttempts;
    private int _fitAttempts;
    
    private float _fitStartingHeight;
    private float _fitBestHeight;
    private float _fitBestError;
    private float _fitVerticalError;
    private float _fitReachError;
    
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

        if (_fittingHeight)
        {
            if (_heightSampleReady)
            {
                ApplyHeightFit();
            }

            return;
        }

        if (_saveRequested)
        {
            _saveRequested = false;
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
        _fittingHeight = false;
        _heightSampleReady = false;
        _saveRequested = false;
        
        _heightFitFeet.Clear();
        
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
    
#region Validation

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

    private bool ValidateMeshes()
    {
        bool valid = true;

        foreach (Node child in GetChildren())
        {
            if (child is not BoneAttachment3D attachment)
            {
                continue;
            }

            if (attachment.IsQueuedForDeletion())
            {
                valid = false;
                continue;
            }

            if (!attachment.Visible)
            {
                if (_debugMode)
                {
                    GD.Print($"[MeshValidation] {attachment.Name} is hidden - toggling visibility...");
                }
                attachment.Show();
                valid = false;
            }

            string boneName = attachment.BoneName;

            if (boneName == "Torso")
            {
                if (BodyDefinition.TorsoMesh != null)
                {
                    valid &= ValidateAttachmentMesh(attachment, "TorsoMesh", CreateTorsoMesh);
                }

                continue;
            }

            if (boneName == "Head")
            {
                if (BodyDefinition.HeadMesh != null)
                {
                    valid &= ValidateAttachmentMesh(attachment, "HeadMesh", CreateHeadMesh);
                }

                continue;
            }

            foreach (LimbDef limb in BodyDefinition.Limbs)
            {
                if (boneName == $"{limb.LimbID}_end")
                {
                    if (limb.EndMesh != null)
                    {
                        valid &= ValidateAttachmentMesh(
                            attachment, 
                            "EndMesh",
                            () => CreateEndMesh(
                                limb.EndMesh,
                                limb.EndMeshOffset,
                                limb.EndMeshRotationDegrees
                            )
                        );
                    }
                    else if (BodyDefinition.JointMesh != null)
                    {
                        string jointName = $"{boneName}_JointMesh";

                        valid &= ValidateAttachmentMesh(
                            attachment,
                            jointName,
                            () => CreateJointMesh(jointName)
                        );
                    }

                    break;
                }

                string prefix = $"{limb.LimbID}_segment_";

                if (!boneName.StartsWith(prefix))
                {
                    continue;
                }

                if (!int.TryParse(boneName.Substring(prefix.Length), out int segmentIndex) ||
                    segmentIndex < 0 ||
                    segmentIndex >= limb.Segments.Count)
                {
                    GD.PrintErr($"[MeshValidation] Invalid segment attachment: {boneName}");
                    valid = false;
                    break;
                }
                
                if (BodyDefinition.JointMesh != null)
                {
                    string jointMeshName = $"{boneName}_JointMesh";

                    valid &= ValidateAttachmentMesh(
                        attachment,
                        jointMeshName,
                        () => CreateJointMesh(jointMeshName)
                    );
                }

                SegmentDef segment = limb.Segments[segmentIndex];
                string segmentMeshName = $"{boneName}_SegmentMesh";
                
                valid &= ValidateAttachmentMesh(
                    attachment,
                    segmentMeshName,
                    () => CreateSegmentMesh(
                        segment,
                        segment.LengthDirection * segment.Length,
                        segmentMeshName
                    )
                );

                break;
            }
        }

        return valid;
    }

    private bool ValidateAttachmentMesh(BoneAttachment3D attachment, string name, Func<MeshInstance3D> createMesh)
    {
        Node node = attachment.GetNodeOrNull<Node>(name);

        if (node != null && node.IsQueuedForDeletion())
        {
            if (_debugMode)
            {
                GD.Print($"[MeshValidation] {attachment.Name}/{name} queued for deletion.");
            }

            return false;
        }

        if (node == null)
        {
            if (_debugMode)
            {
                GD.Print($"[MeshValidation] Recreating {attachment.Name}/{name}");
            }
            // create mesh here using one of the following 
            // CreateTorsoMesh();
            // CreateJointMesh();
            // CreateSegmentMesh();
            // CreateEndMesh();
            // CreateHeadMesh();
            
            MeshInstance3D meshInstance = createMesh();
            
            attachment.AddChild(meshInstance);
            meshInstance.Owner = Owner;
            meshInstance.Show();
            return false; // created this pass - confirm it survives next pass
        }

        if (node is not MeshInstance3D mesh)
        {
            GD.PrintErr($"[MeshValidation] {attachment.Name}/{name} is not a MeshInstance3D");
            return false;
        }

        if (mesh.Mesh == null)
        {
            MeshInstance3D meshInstance = createMesh();

            mesh.Mesh = meshInstance.Mesh;
            mesh.Transform = meshInstance.Transform;
            
            meshInstance.Free(); // just used for mesh + transform

            if (_debugMode)
            {
                GD.Print($"[MeshValidation] Restored mesh resource for {attachment.Name}/{name}");
            }

            return false;
        }
        
        if (!mesh.Visible)
        {
            if (_debugMode)
            {
                GD.Print($"[MeshValidation] {attachment.Name}/{name} is hidden - recreating...");
            }

            attachment.RemoveChild(mesh);
            mesh.QueueFree();
            MeshInstance3D meshInstance = createMesh();
            
            attachment.AddChild(meshInstance);
            meshInstance.Owner = Owner;
            meshInstance.Show();
            return false;
        }

        return true;
    }

    private async System.Threading.Tasks.Task<bool> ValidateMeshesAsync()
    {
        const int maximumPasses = 10;
        const int requiredValidPasses = 2;
        int validPasses = 0;
        

        SceneTree tree = GetTree();

        for (int pass = 0; pass < maximumPasses; pass++)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                return false;
            }

            if (ValidateMeshes())
            {
                if (_debugMode)
                {
                    GD.Print("[MeshValidation] VALID Mesh Pass");
                }
                validPasses++;

                if (validPasses >= requiredValidPasses)
                {
                    if (_debugMode)
                    {
                        GD.Print("[MeshValidation] Meshes Validated");
                    }
                    return true;
                }
            }
            else
            {
                if (_debugMode)
                {
                    GD.Print("[MeshValidation] INVALID Mesh Pass");
                }
                validPasses = 0;
            }
        }
        
        GD.PrintErr("[MeshValidation] Automatic mesh validation did not pass. Automatic saving skipped.");

        return false;
    }
    
#endregion
    
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
        else
        {
            if (_debugMode)
            {
                GD.Print("[Generate] Generation complete - Scene Saved\n");
            }
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
        if (_debugMode)
        {
            GD.Print("\n[Generate] Generation starting...");
        }
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
                    "[IK] " +
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
            _fitTorsoBone = torsoBone;

            if (!await ValidateMeshesAsync())
            {
                return;
            }

            await AutomaticHeightFitAsync();

            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                return;
            }
            
            _saveRequested = true;
            
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
            _heightFitFeet.Add((endBoneIndex, targetNode));
        }

        IterateIK3D IKNode;
        switch (BodyDefinition.InverseKinematicType)
        {
            case BodyDef.IKTypeEnum.CCDIK:
                IKNode = new Ccdik3D
                {
                    Name = $"{limb.LimbID}_CCDIK3D"
                };
                break;
            case BodyDef.IKTypeEnum.FABRIK:
                IKNode = new Fabrik3D
                {
                    Name = $"{limb.LimbID}_FABRIK3D"
                };
                break;
            case BodyDef.IKTypeEnum.JacobianIK:
                IKNode = new JacobianIK3D
                {
                    Name = $"{limb.LimbID}_JacobianIK3D"
                };
                break;
            default:
                IKNode = new Ccdik3D
                {
                    Name = $"{limb.LimbID}_CCDIK3D"
                };
                break;
        }
        
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

    private MeshInstance3D CreateTorsoMesh()
    {
        return new MeshInstance3D
        {
            Name = "TorsoMesh",
            Mesh = BodyDefinition.TorsoMesh,
            Position = -BodyDefinition.TorsoMesh.GetAabb().GetCenter()
        };
    }

    private BoneAttachment3D AddTorsoMesh(int boneIndex)
    {
        BoneAttachment3D attachment = new BoneAttachment3D
        {
            Name = "Torso",
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(attachment);
        attachment.Owner = Owner;

        Mesh mesh = BodyDefinition.TorsoMesh;

        MeshInstance3D meshInstance = CreateTorsoMesh();
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
        meshInstance.Show();
        // MeshDebug(meshInstance);
        return attachment;
    }

    private MeshInstance3D CreateJointMesh(string name)
    {
        MeshInstance3D meshInstance = new MeshInstance3D
        {
            Name = name,
            Mesh = BodyDefinition.JointMesh
        };
        return meshInstance;
    }
    private MeshInstance3D CreateSegmentMesh(SegmentDef segmentDef, Vector3 offset, string name)
    {
        MeshInstance3D meshInstance = new MeshInstance3D
        {
            Name = name,
            Mesh = segmentDef.Mesh,
            RotationDegrees = segmentDef.MeshRotationDegrees,
            Scale = segmentDef.Scale
        };
        
        // account for mesh origin, rotation + scale
        Vector3 meshCenter = segmentDef.Mesh.GetAabb().GetCenter();
        Vector3 transformedCenter = meshInstance.Basis * meshCenter;
        
        meshInstance.Position = offset / 2f - transformedCenter + segmentDef.MeshOffset;

        return meshInstance;
    }
    
    private void AddSegmentMesh(int boneIndex, Vector3 segmentLengthOffset, SegmentDef segmentDef = null)
    {
        var attachment = new BoneAttachment3D
        {
            Name = GetBoneName(boneIndex),
            BoneName = GetBoneName(boneIndex)
        };
        
        AddChild(attachment);
        attachment.Owner = Owner;
        
        if (BodyDefinition.JointMesh != null)
        {
            var joint = CreateJointMesh($"{GetBoneName(boneIndex)}_JointMesh");

            attachment.AddChild(joint);
            joint.Owner = Owner;
            joint.Show();
        }

        if (segmentLengthOffset == Vector3.Zero)
        {
            return; // Intentional joint only attachment for end without endmesh
        }

        if (segmentDef?.Mesh == null)
        {
            GD.PrintErr("Segment mesh is null");
            return;
        }

        MeshInstance3D meshInstance =
            CreateSegmentMesh(segmentDef, segmentLengthOffset, $"{GetBoneName(boneIndex)}_SegmentMesh");
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
        
        // MeshDebug(meshInstance);
        
        meshInstance.Show();
    }

    private MeshInstance3D CreateEndMesh(Mesh mesh, Vector3 offset, Vector3 rotationDegrees)
    {
        MeshInstance3D meshInstance = new MeshInstance3D
        {
            Name = "EndMesh",
            Mesh = mesh,
            Position = offset,
            RotationDegrees = rotationDegrees
        };
        return meshInstance;
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

        MeshInstance3D meshInstance = CreateEndMesh(mesh, offset, endMeshRotationDegrees);
        
        attachment.AddChild(meshInstance);
        meshInstance.Owner = Owner;
        
        // MeshDebug(meshInstance);
        
        meshInstance.Show();
    }

    private MeshInstance3D CreateHeadMesh()
    {
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
        return meshInstance;
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

        MeshInstance3D meshInstance = CreateHeadMesh();
        
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

    private async System.Threading.Tasks.Task AutomaticHeightFitAsync()
    {
        if (!Engine.IsEditorHint() || _heightFitFeet.Count == 0)
        {
            return;
        }
        
        BeginHeightFit();

        SceneTree tree = GetTree();
        int idleFrames = 0;
        const int maximumIdleFrames = 180;

        while (_fittingHeight)
        {
            int previousAttempts = _fitAttempts;
            int previousUpdates = _fitSettleUpdates;

            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            if (!IsInstanceValid(this) || !IsInsideTree())
            {
                return;
            }

            if (!_fittingHeight)
            {
                break;
            }

            bool progressed = _fitAttempts != previousAttempts || _fitSettleUpdates != previousUpdates;

            idleFrames = progressed ? 0 : idleFrames + 1;

            if (idleFrames >= maximumIdleFrames)
            {
                if (_debugMode)
                {
                    GD.Print("Height fitting reached max idle frames. Keeping best known height.");
                }
                
                FinishHeightFit(false);
                break;
            }
        }
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
        if (!_fittingHeight || _heightSampleReady)
        {
            return;
        }
        
        _fitSettleUpdates++;

        if (_fitSettleUpdates < FitSettleUpdates)
        {
            return;
        }

        _fitVerticalError = float.NegativeInfinity;
        _fitReachError = 0f;

        foreach (var foot in _heightFitFeet)
        {
            Vector3 actual = GetBoneGlobalPose(foot.Bone).Origin;
            Vector3 preferred = ToLocal(foot.target.GlobalPosition);

            // find lowest foot point
            _fitVerticalError = Mathf.Max(
                _fitVerticalError,
                preferred.Y - actual.Y
            );

            // full distance
            _fitReachError = Mathf.Max(
                _fitReachError,
                actual.DistanceTo(preferred)
            );
        }

        _heightSampleReady = true;
    }

    private void ApplyHeightFit()
    {
        _heightSampleReady = false;

        if (!float.IsFinite(_fitVerticalError) || !float.IsFinite(_fitReachError))
        {
            FinishHeightFit(false);
            return;
        }

        float currentHeight = GetBoneRest(_fitTorsoBone).Origin.Y;

        if (_fitReachError < _fitBestError)
        {
            bool improved = _fitBestError - _fitReachError > 0.0001f;

            _fitBestError = _fitReachError;
            _fitBestHeight = currentHeight;

            if (improved)
            {
                _fitStalledAttempts = 0;
            }
            else
            {
                _fitStalledAttempts++;
            }
        }
        else
        {
            _fitStalledAttempts++;
        }

        float tolerance = Mathf.Max(_heightFitTolerance, 0.001f);

        if (_debugMode)
        {
            GD.Print(
                $"[HeightFit] height={currentHeight:F3}, " +
                $"veritcalError={_fitVerticalError:F3}, " +
                $"reachError={_fitReachError:F3}"
            );
        }

        if (_fitReachError <= tolerance)
        {
            FinishHeightFit(true);
            return;
        }

        if (_fitAttempts >= FitMaximumAttempts ||
            _fitStalledAttempts >= 5)
        {
            FinishHeightFit(false);
            return;
        }

        float correction = Mathf.Clamp(
            _fitVerticalError * 0.5f,
            -0.1f,
            0.1f
        );

        float maximumAdjustment = Mathf.Max(_maximumHeightAdjustment, 0f);

        float nextHeight = Mathf.Clamp(
            currentHeight + correction,
            _fitStartingHeight - maximumAdjustment,
            _fitStartingHeight + maximumAdjustment
        );

        if (Mathf.Abs(nextHeight - currentHeight) < 0.0001f)
        {
            FinishHeightFit(false);
            return;
        }

        SetFittedTorsoHeight(nextHeight);

        _fitAttempts++;
        _fitSettleUpdates = 0;
    }
    private void BeginHeightFit()
    {
        if (!Engine.IsEditorHint() || _fittingHeight)
        {
            return;
        }

        if (_fitTorsoBone < 0 || _fitTorsoBone >= GetBoneCount())
        {
            GD.PrintErr("Generate body before adjusting height");
            return;
        }

        if (_heightFitFeet.Count == 0)
        {
            return;
        }
        
        _fitStartingHeight = GetBoneRest(_fitTorsoBone).Origin.Y;
        _fitBestHeight = _fitStartingHeight;
        _fitBestError = float.PositiveInfinity;

        _fitAttempts = 0;
        _fitStalledAttempts = 0;
        _fitSettleUpdates = 0;

        _heightSampleReady = false;
        _fittingHeight = true;

        DisconnectHeightFit();
        SkeletonUpdated += MeasureHeightFit;
    }

    private void SetFittedTorsoHeight(float height)
    {
        Transform3D rest = GetBoneRest(_fitTorsoBone);
        rest.Origin.Y = height;
        
        SetBoneRest(_fitTorsoBone, rest);
        ResetBonePose(_fitTorsoBone);
    }

    private void FinishHeightFit(bool reachedTargets)
    {
        DisconnectHeightFit();

        _fittingHeight = false;
        _heightSampleReady = false;

        if (!reachedTargets)
        {
            SetFittedTorsoHeight(_fitBestHeight);

            GD.Print(
                "Height fitting could not reach every foot target.\nJoint limits or segment angles may need adjusting.");
        }
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
    }
}
