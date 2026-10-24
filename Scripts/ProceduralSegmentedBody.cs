using Godot;

[Tool, GlobalClass]
public partial class ProceduralSegmentedBody : Node3D
{
    // === Params ===

    [ExportGroup("Mesh Inputs")] 
    [Export] private Mesh HeadMesh = null;
    [Export] private Mesh BodyMesh = null;
    [Export] private Mesh LimbMesh = null;
    [Export] private Mesh FootMesh = null;
    [Export] private Mesh JointMesh = null;

    [ExportGroup("Materials")] 
    [Export] private Material HeadMaterialOverride = null;
    [Export] private Material BodyMaterialOverride = null;
    [Export] private Material LimbMaterialOverride = null;
    [Export] private Material FootMaterialOverride = null;
    [Export] private Material JointMaterialOverride = null;

    [ExportGroup("Body Structure")] 
    [Export] private float HeadScale = 1.0f;

    // === Methods ===
    private void Generate()
    {
    
    }

    private void PlaceBody()
    {

    }

    private void PlaceFeet()
    {

    }

    private void PlaceLimbs()
    {
        
    }

    private void PlaceHead()
    {
        
    }

}
