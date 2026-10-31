using Godot;

[GlobalClass, Tool]
public partial class Util : GodotObject
{
    public static Vector3 Vector3DegToRad(Vector3 vector)
    {
        return vector * (Mathf.Pi / 180f);
    }
}
