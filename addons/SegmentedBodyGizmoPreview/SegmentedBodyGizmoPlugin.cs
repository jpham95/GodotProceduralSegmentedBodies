#if TOOLS
using System.Collections.Generic;
using Godot;

[Tool]
public partial class SegmentedBodyGizmoPlugin : EditorNode3DGizmoPlugin
{
    private static Color PREVIEW_COLOUR = new(0.2f, 0.85f, 1f);
    private static string PREVIEW_MATERIAL_NAME = "preview_material";
    private static string GIZMO_NAME = "Segmented Body Preview";

    private SphereMesh _jointMesh = new()
    {
        Radius = 0.12f,
        Height = 0.24f,
        RadialSegments = 16,
        Rings = 8
    };
    
    public SegmentedBodyGizmoPlugin()
    {
        CreateMaterial(PREVIEW_MATERIAL_NAME, PREVIEW_COLOUR);
    }

    public override bool _HasGizmo(Node3D forNode3D)
    {
        return forNode3D is ProceduralSegmentedBody;
    }

    public override string _GetGizmoName()
    {
        return GIZMO_NAME;
    }

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();

        if (gizmo.GetNode3D() is not ProceduralSegmentedBody body)
        {
            return;
        }

        if (!body.ShowPreview)
        {
            return;
        }

        Vector3[] previewLines = body.GetPreview(out List<Vector3> jointPositions);
        Material material = GetMaterial(PREVIEW_MATERIAL_NAME, gizmo);
        
        if (previewLines.Length == 0)
        {
            return;
        }

        gizmo.AddLines(previewLines, material);
        gizmo.AddCollisionSegments(previewLines);

        foreach (Vector3 position in jointPositions)
        {
            gizmo.AddMesh(
                _jointMesh,
                material,
                new Transform3D(Basis.Identity, position)
            );
        }
    }
}
#endif