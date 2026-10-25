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

    public Vector3 LimbDirection
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
    
    [Export] public MeshLengthAxis LengthAxis = MeshLengthAxis.Y;
    [Export] public Vector3 Scale = Vector3.One; 
    [Export] public Vector3 RestRotationDegrees = Vector3.Zero;
    [Export] public Mesh Mesh = null;
    [Export] public Vector3 MeshOffset = Vector3.Zero;
    [Export] public Vector3 MeshRotationDegrees = Vector3.Zero;
    // [Export] public bool LimitRotation = false;
    // [Export] public Quaternion LimitationRotationOffset = new Quaternion(0, 0, 0, 0);
}
