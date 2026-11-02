using Godot;
using Vector3 = Godot.Vector3;

[Tool, GlobalClass]
public partial class SegmentDef : Resource
{
    public enum MeshLengthAxis
    {
        X,
        Y,
        Z
    }
    public float Length
    {
        get
        {
            if (this.Mesh == null)
            {
                return 0f;
            }
            Vector3 meshSize = this.Mesh.GetAabb().Size;
            switch (this.LengthAxis)
            {
                case MeshLengthAxis.Y:
                    return meshSize.Y * this.Scale.Y;
                case MeshLengthAxis.X:
                    return meshSize.X * this.Scale.X;
                case MeshLengthAxis.Z:
                    return meshSize.Z * this.Scale.Z;
                default:
                    return 0f;
            }
        }
    }

    public Vector3 LengthDirection
    {
        get
        {
            switch (this.LengthAxis)
            {
                case MeshLengthAxis.Y:
                    return Vector3.Down;
                case MeshLengthAxis.X:
                    return Vector3.Left;
                case MeshLengthAxis.Z:
                    return Vector3.Forward;
                default:
                    return Vector3.Zero;
            }
        }
    }

    public enum JointPresetEnum
    {
        Free,
        Hip,
        Knee,
        Custom
    }
    [ExportGroup("Mesh")]
    [Export] public Mesh Mesh = null;
    [Export] public MeshLengthAxis LengthAxis = MeshLengthAxis.Y;
    [Export] public Vector3 MeshOffset = Vector3.Zero;
    [Export] public Vector3 MeshRotationDegrees = Vector3.Zero;
    [Export] public Vector3 Scale = Vector3.One; 
    [Export] public Vector3 RestRotationDegrees = Vector3.Zero;
    
    [ExportGroup("Joint Limitations")]
    [Export] public JointPresetEnum JointPreset = JointPresetEnum.Free;
    [ExportSubgroup("Custom Settings")]
    [Export] public bool LimitJoint = false;
    [Export] public SkeletonModifier3D.RotationAxis JointRotationAxis = SkeletonModifier3D.RotationAxis.X;
    [Export(PropertyHint.Range, "0,360,1")] public float JointConeAngleDegrees = 90f;
    [Export] public Vector3 JointLimitRotationDegrees = Vector3.Zero;
    // [Export] public Quaternion LimitationRotationOffset = new Quaternion(0, 0, 0, 0);
}
