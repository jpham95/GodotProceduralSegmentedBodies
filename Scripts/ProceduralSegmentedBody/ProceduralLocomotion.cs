using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class ProceduralLocomotion : Node3D
{
    /// <summary>
    /// Record the current IK target positions as preferred stance positions for this generated body.
    /// Regenerating replaces these recorded positions.
    /// </summary>
    [ExportToolButton("Set Current Target Positions as Rest Position")]
    private Callable SetTargetPositionsAsRestPositions => Callable.From(UpdateRestPositions);

    /// <summary>
    /// Swap saved stance positions with previous values recorded this editor session.
    /// </summary>
    [ExportToolButton("Undo Target Position Changes")]
    private Callable UndoTargetPositionChanges => Callable.From(UndoRestPositionChanges);

    public LocomotionSettings settings;

    /// <summary>
    /// Generated links between grounded limbs, IK targets and ground probes. Recreated during generation.
    /// Exported to save editor changes for runtime use. 
    /// </summary>
    [ExportCategory("Generated Limbs")]
    [Export] public Godot.Collections.Array<LocomotionLimbDef> Limbs = new();
    
    private class AnimatedLimb
    {
        public LocomotionLimbDef Definition;
        public Marker3D Target;
        public RayCast3D StanceRay;
        public RayCast3D LandingRay;

        public bool Grounded;
        public bool Stepping;
        public float Progress;

        public Vector3 Position;
        public Quaternion Rotation;

        public Vector3 StartPosition;
        public Quaternion StartRotation;

        public Vector3 EndPosition;
        public Quaternion EndRotation;

        public void ForceRaycastUpdates()
        {
            this.StanceRay.ForceRaycastUpdate();
            this.LandingRay.ForceRaycastUpdate();
        }
    }
    
    private List<AnimatedLimb> _limbs = new();

    private ProceduralSegmentedBody _body;
    private Vector3 _previousBodyPosition;
    private bool _initialised = false;
    private int _nextLimb;
    private uint _nextStepGroup = 1;
    private float _previousBodyYaw;
    
    public override void _Ready()
    {
        // prevent physics process in-engine
        // because of [Tool] tag
        if (Engine.IsEditorHint())
        {
            SetPhysicsProcess(false);
            return;
        }
        Callable.From(Initialise).CallDeferred();
    }

    private void Initialise()
    {
        _body = GetParent() as ProceduralSegmentedBody;
        
        if (_body == null)
        {
            GD.PrintErr("ProceduralSegmentedBody parent missing");
            SetPhysicsProcess(false);
            return;
        }

        settings = _body.BodyDefinition.LocomotionSettings;
        _previousBodyYaw = _body.GlobalRotation.Y;
        InitialiseLimbs();
    }
    
    private void InitialiseLimbs()
    {

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

            RayCast3D stanceRay = GetNodeOrNull<RayCast3D>(locomotionLimbDef.StanceRayPath);
            RayCast3D landingRay = GetNodeOrNull<RayCast3D>(locomotionLimbDef.LandingRayPath);
            
            if (stanceRay == null)
            {
                GD.PrintErr($"Missing stance raycast at {locomotionLimbDef.StanceRayPath}");
                continue;
            }
            
            if (landingRay == null)
            {
                GD.PrintErr($"Missing landing raycast at {locomotionLimbDef.LandingRayPath}");
                continue;
            }
            
            _limbs.Add(new AnimatedLimb
            {
                Definition = locomotionLimbDef,
                Target = target,
                Position = target.GlobalPosition,
                // https://docs.godotengine.org/en/stable/classes/class_basis.html#class-basis-method-orthonormalized
                // prevent rounding errors by using Orthonormalized()
                Rotation = target.GlobalBasis.Orthonormalized().GetRotationQuaternion(),
                // bools default to false
                // progress defaults to 0
                // start and end pos default to Vector3.Zero
                // start and end rot default to new Quaternion(0, 0, 0, 0)
                // zero quat is okay because it is set before use
                StanceRay = stanceRay,
                LandingRay = landingRay
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

        ProceduralSegmentedBody body = GetParent<ProceduralSegmentedBody>();
        if (body == null)
        {
            GD.PrintErr("ProceduralSegmentedBody parent missing.");
            return;
        }

        RayCast3D stanceRay = CreateRaycastNode($"{limb.LimbID}_StanceRaycast");
        RayCast3D landingRay = CreateRaycastNode($"{limb.LimbID}_LandingRaycast");

        Vector3 preferred = body.ToGlobal(restPosition);

        PositionRaycastNode(stanceRay, preferred);
        
        Vector3 forward = -body.GlobalBasis.Z;
        forward.Y = 0f;
        forward = forward.Normalized();

        PositionRaycastNode(landingRay, preferred + forward * settings.StepAheadDistance);
        Limbs.Add(new LocomotionLimbDef
        {
            ResourceName = limb.LimbID,
            Definition = limb,
            TargetPath = GetPathTo(target),
            StanceRayPath = GetPathTo(stanceRay),
            LandingRayPath = GetPathTo(landingRay),
            RestPosition = restPosition,
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
        
        bool isMoving = velocity.LengthSquared() > 0.0001f;

        float currentYaw = _body.GlobalRotation.Y;
        float yawChange = Mathf.AngleDifference(_previousBodyYaw, currentYaw);
        _previousBodyYaw = currentYaw;

        bool isTurning = Mathf.Abs(yawChange) > Mathf.DegToRad(1f) * deltaTime;
        
        Vector3 movementDirection = isMoving
            ? velocity.Normalized()
            : Vector3.Zero;

        Vector3 bodyForward = _body.GlobalBasis.Z;
        bodyForward.Y = 0f;
        bodyForward = bodyForward.Normalized();

        float backwardAmount = Mathf.Clamp(
            -movementDirection.Dot(bodyForward),
            0f,
            1f
        );

        float stepMultiplier = Mathf.Lerp(
            1f,
            settings.BackwardStepMultiplier,
            backwardAmount
        );
        
        Vector3 lookaheadOffset = movementDirection * Mathf.Max(settings.StepAheadDistance, 0f) * stepMultiplier;

        bool steppingHappeningRN = false;

        foreach (AnimatedLimb limb in _limbs)
        {
            Vector3 preferred = _body.ToGlobal(limb.Definition.RestPosition);
            
            PositionRaycastNode(limb.StanceRay, preferred);
            PositionRaycastNode(limb.LandingRay, preferred + lookaheadOffset);
            
            limb.ForceRaycastUpdates();
            
            // initial placement
            if (!limb.Grounded)
            {
                // Vector3 preferred = _body.ToGlobal(limb.Definition.RestPosition);

                if (GetFootPlacement(
                        limb,
                        limb.StanceRay,
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
        // only step when moving
        if (settings.AnimateLocomotion && 
            !steppingHappeningRN)
        {
            if (settings.UseStepGroups)
            {
                StartGroupStep(movementDirection, isTurning);
            }
            else
            {
                StartStep(movementDirection, isTurning);
            }
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

    private void StartStep(Vector3 movementDirection, bool isTurning)
    {
        for (int offset = 0; offset < _limbs.Count; offset++)
        {
            int index = (_nextLimb + offset) % _limbs.Count;
            if (TryStartLimbStep(_limbs[index], movementDirection, isTurning))
            {
                _nextLimb = (index + 1) % _limbs.Count;
                return;
            }
        }
    }

    private void StartGroupStep(Vector3 movementDirection, bool isTurning)
    {
        uint groupCount = settings.StepGroupCount;

        // try each group at most once per tick
        for (uint attempt = 0; attempt < groupCount; attempt++)
        {
            if (_nextStepGroup > groupCount)
            {
                _nextStepGroup = 1;
            }

            uint group = _nextStepGroup;
            _nextStepGroup = group == groupCount ? 1 : group + 1;

            bool anyStarted = false;

            foreach (AnimatedLimb limb in _limbs)
            {
                if (limb.Definition.Definition.StepGroup != group)
                {
                    continue;
                }

                if (TryStartLimbStep(limb, movementDirection, isTurning))
                {
                    anyStarted = true;
                }
            }

            if (anyStarted)
            {
                return;
            }
        }
    }

    private bool TryStartLimbStep(AnimatedLimb limb, Vector3 movementDirection, bool isTurning)
    {
        bool isMoving = movementDirection.LengthSquared() > 0.0001f;
        
        if (!limb.Grounded)
        {
            return false;
        }

        if (!GetFootPlacement(
                limb,
                limb.StanceRay,
                out Vector3 destination,
                out Quaternion rotation))
        {
            return false;
        }
        
        Vector3 displacement = destination - limb.Position;
        displacement.Y = 0f;

        float lag = displacement.Dot(movementDirection);
        float angle = Mathf.RadToDeg(limb.Rotation.AngleTo(rotation));

        Vector3 stanceError = destination - limb.Position;
        stanceError.Y = 0f;

        bool needsMovementStep = isMoving && lag >= settings.StepTriggerDistance;

        bool needsTurningStep = angle >= settings.TurnThresholdDegrees ||
                                (isTurning && stanceError.Length() >= settings.StepTriggerDistance);

        if (!needsMovementStep && !needsTurningStep)
        {
            return false;
        }

        if (GetFootPlacement(
                limb,
                limb.LandingRay,
                out Vector3 predictedPosition,
                out Quaternion predictedRotation))
        {
            destination = predictedPosition;
            rotation = predictedRotation;
        }
        
        float landingDistance = limb.Position.DistanceTo(destination);
        float landingAngle = Mathf.RadToDeg(limb.Rotation.AngleTo(rotation));

        if (landingDistance < settings.StepTriggerDistance &&
            landingAngle < settings.TurnThresholdDegrees)
        {
            return false;
        }
        
        // successful step found - set params
        limb.StartPosition = limb.Position;
        limb.StartRotation = limb.Rotation;
        limb.EndPosition = destination;
        limb.EndRotation = rotation;
        limb.Progress = 0f;
        limb.Stepping = true;

        return true;
    }
    
    private void AdvanceStep(AnimatedLimb limb, float deltaTime)
    {
        limb.Progress = Mathf.Min(
            limb.Progress + deltaTime / Mathf.Max(settings.StepDuration, 0.01f),
            1f
        );

        float t = limb.Progress;

        float smoothedT = Mathf.Clamp(settings.StepProgressCurve.Sample(t), 0f, 1f);

        limb.Position = limb.StartPosition.Lerp(
            limb.EndPosition, smoothedT
        );

        float lift = Mathf.Clamp(settings.StepLiftCurve.Sample(t), 0f, 1f);
        limb.Position += Vector3.Up * lift * settings.StepHeight;

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
        // Vector3 preferredPosition,
        RayCast3D raycastNode,
        out Vector3 anklePosition,
        out Quaternion rotation)
    {
        anklePosition = Vector3.Zero;
        rotation = Quaternion.Identity;

        if (!raycastNode.IsColliding())
        {
            return false;
        }

        Vector3 contact = raycastNode.GetCollisionPoint();
        Vector3 normal = raycastNode.GetCollisionNormal();

        if (normal.LengthSquared() < 0.0001f)
        {
            return false;
        }

        // minimum dot product from max slope setting
        float minimumUpDot = Mathf.Cos(
            Mathf.DegToRad(settings.MaximumSlopeDegrees)
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

        anklePosition = contact + normal * (definition.AnkleToContactDistance + definition.GroundClearance);
        
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
        if (settings.StepProgressCurve == null)
        {
            err += "StepProgressCurve not found.\n";
        } 
        else if (settings.StepProgressCurve.GetPointCount() < 2)
        {
            err += "StepProgressCurve requires at least 2 points.\n";
        }
        if (settings.StepLiftCurve == null)
        {
            err += "StepLiftCurve not found.\n";
        }
        else if (settings.StepLiftCurve.GetPointCount() < 2)
        {
            err += "StepLiftCurve requires at least 2 points.";
        }

        return err.Length > 0 ? err : null;
    }

    private void PositionRaycastNode(RayCast3D raycastNode, Vector3 preferredPosition)
    {
        Vector3 from = preferredPosition + Vector3.Up * settings.ProbeHeight;
        Vector3 to = preferredPosition + Vector3.Down * settings.ProbeDepth;

        raycastNode.GlobalPosition = from;
        raycastNode.TargetPosition = raycastNode.ToLocal(to);
        raycastNode.CollisionMask = settings.GroundCollisionMask;
    }

    private RayCast3D CreateRaycastNode(string name)
    {
        RayCast3D raycastNode = new RayCast3D
        {
            Name = name,
            CollisionMask = settings.GroundCollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Enabled = false
        };
        
        AddChild(raycastNode);
        raycastNode.Owner = Owner;
        
        return raycastNode;
    }
}
