#if TOOLS
using Godot;

[Tool]
public partial class SegmentedBodyGizmoPlugin : EditorNode3DGizmoPlugin
{
    private static Color PREVIEW_COLOUR = new(0.2f, 0.85f, 1f);
    private static string PREVIEW_MATERIAL_NAME = "preview_material";
    private static string GIZMO_NAME = "Segmented Body Preview";
    
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

        Vector3[] previewLines = body.GetPreview();

        if (previewLines.Length == 0)
        {
            return;
        }

        gizmo.AddLines(previewLines, GetMaterial(PREVIEW_MATERIAL_NAME, gizmo));
        
        gizmo.AddCollisionSegments(previewLines);
    }
}
#endif