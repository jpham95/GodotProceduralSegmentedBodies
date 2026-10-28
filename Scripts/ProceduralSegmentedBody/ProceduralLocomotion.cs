using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class ProceduralLocomotion : Node3D
{
    [ExportToolButton("Set Current Target Positions as Rest Position")]
    private Callable SetTargetPositionsAsRestPositions => Callable.From(UpdateRestPositions);

    [ExportToolButton("Undo Targer Position Changes")]
    private Callable UndoTargetPositionChanges => Callable.From(UndoRestPositionChanges);
    
    [ExportGroup("Locomotion")] 
    [Export] public bool AnimateLocomotion = true;
    [Export] public float StepDistance = 0.35f;
    [Export] public float StepDuration = 0.25f;
    [Export] public float StepHeight = 0.2f;
    [Export] public float PredictionTime = 0.1f;
    [Export] public float MaximumPrediction = 0.25f;
    [Export] public float TurnThresholdDegrees = 30f;
    [Export] public Curve StepProgressCurve;
    [Export] public Curve StepLiftCurve;
    
    [ExportGroup("Ground Detection")] 
    [Export] public uint GroundCollisionMask = 1;

    [Export] public float ProbeHeight = 1f;
    [Export] public float ProbeDepth = 2f;
    [Export] public float MaximumSlopeDegrees = 50f;
    
    [ExportCategory("Generated Limbs")]
    [Export] public Godot.Collections.Array<LocomotionLimbDef> Limbs = new();
    private class AnimatedLimb
    {
        public LocomotionLimbDef Definition;
        public Marker3D Target;

        public bool Grounded;
        public bool Stepping;
        public float Progress;

        public Vector3 Position;
        public Quaternion Rotation;

        public Vector3 StartPosition;
        public Quaternion StartRotation;

        public Vector3 EndPosition;
        public Quaternion EndRotation;
    }
    
    private List<AnimatedLimb> _limbs = new();

    private ProceduralSegmentedBody _body;
    private Vector3 _previousBodyPosition;
    private bool _initialised = false;
    private int _nextLimb;
    
    public override void _Ready()
    {
        // prevent physics process in-engine
        // because of [Tool] tag
        if (Engine.IsEditorHint())
        {
            SetPhysicsProcess(false);
            return;
        }
        Callable.From(InitialiseLimbs).CallDeferred();
    }

    public void InitialiseCurves()
    {
        _body = GetParent() as ProceduralSegmentedBody;
        
        if (_body == null)
        {
            GD.PrintErr("ProceduralSegmentedBody parent missing");
            SetPhysicsProcess(false);
            return;
        }
        
        if (StepProgressCurve == null && _body.DefaultStepProgressCurve != null)
        {
            StepProgressCurve = (Curve)_body.DefaultStepProgressCurve.Duplicate();
        }
        if (StepLiftCurve == null && _body.DefaultStepLiftCurve != null)
        {
            StepLiftCurve = (Curve)_body.DefaultStepLiftCurve.Duplicate();
        }
    }
    
    private void InitialiseLimbs()
    {
        _body = GetParent() as ProceduralSegmentedBody;
        
        if (_body == null)
        {
            GD.PrintErr("ProceduralSegmentedBody parent missing");
            SetPhysicsProcess(false);
            return;
        }
        
        InitialiseCurves();

        string curveErr = ValidateStepCurves();

        if (curveErr != null)
        {
            GD.PrintErr(curveErr);
            SetPhysicsProcess(false);
            return;
        }
        
        _limbs.Clear();

        foreach (LocomotionLimbDef locomotionLimbDef in Limbs)
        {
            if (locomotionLimbDef?.Definition == null ||
                !locomotionLimbDef.Definition.GroundFoot)
            {
                continue;
            }

            Marker3D target = GetNodeOrNull<Marker3D>(locomotionLimbDef.TargetPath);

            if (target == null)
            {
                GD.PrintErr($"Missing locomotion target: {locomotionLimbDef.TargetPath}");
                continue;
            }
            
            _limbs.Add(new AnimatedLimb
            {
                Definition = locomotionLimbDef,
                Target = target,
                Position = target.GlobalPosition,
                // https://docs.godotengine.org/en/stable/classes/class_basis.html#class-basis-method-orthonormalized
                // prevent rounding errors by using Orthonormalized()
                Rotation = target.GlobalBasis.Orthonormalized().GetRotationQuaternion()
                // bools default to false
                // progress defaults to 0
                // start and end pos default to Vector3.Zero
                // start and end rot default to new Quaternion(0, 0, 0, 0)
                // zero quat is okay because it is set before use
            });
        }

        _previousBodyPosition = _body.GlobalPosition;
        _initialised = true;
    }
    public void AddLimb(LimbDef limb, Marker3D target, Vector3 restPosition)
    {
        if (!limb.GroundFoot)
        {
            return;
        }
        
        Limbs.Add(new LocomotionLimbDef
        {
            ResourceName = limb.LimbID,
            Definition = limb,
            TargetPath = GetPathTo(target),
            RestPosition = restPosition,
            PhaseOffset = 0f
        });
    }

    private void ResetTargets()
    {
        foreach (AnimatedLimb limb in _limbs)
        {
            limb.Target.Position = limb.Definition.RestPosition;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint() || !_initialised)
        {
            return;
        }

        float deltaTime = (float)delta;

        Vector3 bodyPosition = _body.GlobalPosition;
        Vector3 velocity = deltaTime > 0f 
            ? (bodyPosition - _previousBodyPosition) / deltaTime 
            : Vector3.Zero;

        _previousBodyPosition = bodyPosition;

        velocity.Y = 0f;

        Vector3 prediction = velocity * PredictionTime;

        float predictionLimit = Mathf.Max(MaximumPrediction, 0f);
        if (prediction.Length() > predictionLimit)
        {
            prediction = prediction.Normalized() * predictionLimit;
        }

        bool steppingHappeningRN = false;

        foreach (AnimatedLimb limb in _limbs)
        {
            // initial placement
            if (!limb.Grounded)
            {
                Vector3 preferred = _body.ToGlobal(limb.Definition.RestPosition);

                if (GetFootPlacement(
                        limb,
                        preferred,
                        out Vector3 position,
                        out Quaternion rotation))
                {
                    limb.Position = position;
                    limb.Rotation = rotation;
                    limb.Grounded = true;
                }
            }
            
            // finish existing step
            if (limb.Stepping)
            {
                AdvanceStep(limb, deltaTime);
            }

            steppingHappeningRN = steppingHappeningRN || limb.Stepping;
        }

        // one foot at a time for now
        if (AnimateLocomotion && !steppingHappeningRN)
        {
            StartStep(prediction);
        }
        
        // rewrite global transforms to keep limb positions
        // independent of parent position
        foreach (AnimatedLimb limb in _limbs)
        {
            limb.Target.GlobalTransform = new Transform3D(
                new Basis(limb.Rotation),
                limb.Position
            );
        }
    }

    private void StartStep(Vector3 prediction)
    {
        for (int offset = 0; offset < _limbs.Count; offset++)
        {
            int index = (_nextLimb + offset) % _limbs.Count;
            AnimatedLimb limb = _limbs[index];

            if (!limb.Grounded)
            {
                continue;
            }

            Vector3 preferred = _body.ToGlobal(limb.Definition.RestPosition);

            if (!GetFootPlacement(
                    limb,
                    preferred,
                    out Vector3 destination,
                    out Quaternion rotation))
            {
                continue;
            }

            float distance = limb.Position.DistanceTo(destination);
            float angle = Mathf.RadToDeg(limb.Rotation.AngleTo(rotation));

            if (distance < StepDistance && angle < TurnThresholdDegrees)
            {
                continue;
            }

            if (GetFootPlacement(
                    limb,
                    preferred + prediction,
                    out Vector3 predictedPosition,
                    out Quaternion predictedRotation))
            {
                destination = predictedPosition;
                rotation = predictedRotation;
            }

            // successful step found - set params
            limb.StartPosition = limb.Position;
            limb.StartRotation = limb.Rotation;
            limb.EndPosition = destination;
            limb.EndRotation = rotation;
            limb.Progress = 0f;
            limb.Stepping = true;

            _nextLimb = (index + 1) % _limbs.Count;
            break;
        }
    }

    private void AdvanceStep(AnimatedLimb limb, float deltaTime)
    {
        limb.Progress = Mathf.Min(
            limb.Progress + deltaTime / Mathf.Max(StepDuration, 0.01f),
            1f
        );

        float t = limb.Progress;

        float smoothedT = Mathf.Clamp(StepProgressCurve.Sample(t), 0f, 1f);

        limb.Position = limb.StartPosition.Lerp(
            limb.EndPosition, smoothedT
        );

        float lift = Mathf.Clamp(StepLiftCurve.Sample(t), 0f, 1f);
        limb.Position += Vector3.Up * lift * StepHeight;

        // lerp foot rotation to match landing rotation
        limb.Rotation = limb.StartRotation.Slerp(limb.EndRotation, smoothedT);

        if (t >= 1f)
        {
            limb.Position = limb.EndPosition;
            limb.Rotation = limb.EndRotation;
            limb.Stepping = false;
        }
    }

    private bool GetFootPlacement(
        AnimatedLimb limb, 
        Vector3 preferredPosition, 
        out Vector3 anklePosition,
        out Quaternion rotation)
    {
        anklePosition = Vector3.Zero;
        rotation = Quaternion.Identity;

        Vector3 from = preferredPosition + Vector3.Up * ProbeHeight;

        Vector3 to = preferredPosition + Vector3.Down * ProbeDepth;

        var query = PhysicsRayQueryParameters3D.Create(
            from, to, GroundCollisionMask
        );

        query.CollideWithAreas = false;

        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (hit.Count == 0)
        {
            return false;
        }

        Vector3 contact = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3().Normalized();

        // minimum dot product from max slope setting
        float minimumUpDot = Mathf.Cos(
            Mathf.DegToRad(MaximumSlopeDegrees)
        );

        // if normal dot product with up vector exceeds min dot product
        // no valid footing
        if (normal.Dot(Vector3.Up) < minimumUpDot)
        {
            return false;
        }
        
        // project forward direction onto ground plane
        // forward = -Z
        Vector3 forward = -_body.GlobalBasis.Z;
        forward -= normal * forward.Dot(normal);

        // prevent in-ground or above-ground footings
        if (forward.LengthSquared() < 0.0001f)
        {
            return false;
        }

        forward = forward.Normalized();

        Vector3 right = forward.Cross(normal).Normalized();

        Basis footBasis = new Basis(right, normal, -forward);
        rotation = footBasis.GetRotationQuaternion();

        LimbDef definition = limb.Definition.Definition;

        anklePosition = contact + normal * (definition.AnkleToSoleDistance + definition.GroundClearance);
        
        return true;
    }

    private void UpdateRestPositions()
    {
        // only run in editor mode
        if (!Engine.IsEditorHint())
        {
            return;
        }

        ProceduralSegmentedBody body = GetParent() as ProceduralSegmentedBody;

        if (body == null)
        {
            GD.PrintErr("ProceduralSegmentedBody parent missing");
            return;
        }

        foreach (LocomotionLimbDef limb in Limbs)
        {
            if (limb == null)
            {
                continue;
            }

            Marker3D target = GetNodeOrNull<Marker3D>(limb.TargetPath);

            if (target == null)
            {
                continue;
            }

            limb.PreviousRestPosition = limb.RestPosition;
            limb.RestPosition = body.ToLocal(target.GlobalPosition);
        }
    }

    private void UndoRestPositionChanges()
    {
        foreach (LocomotionLimbDef limb in Limbs)
        {
            (limb.RestPosition, limb.PreviousRestPosition) = (limb.PreviousRestPosition, limb.RestPosition);
        }
    }

    private string ValidateStepCurves()
    {
        string err = "";
        if (StepProgressCurve == null)
        {
            err += "StepProgressCurve not found.\n";
        } 
        else if (StepProgressCurve.GetPointCount() < 2)
        {
            err += "StepProgressCurve requires at least 2 points.\n";
        }
        if (StepLiftCurve == null)
        {
            err += "StepLiftCurve not found.\n";
        }
        else if (StepLiftCurve.GetPointCount() < 2)
        {
            err += "StepLiftCurve requires at least 2 points.";
        }

        return err.Length > 0 ? err : null;
    }
}
