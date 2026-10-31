#if TOOLS
using Godot;

[Tool]
public partial class SegmentedBodyEditorPlugin : EditorPlugin
{
    private SegmentedBodyGizmoPlugin _gizmoPlugin;

    public override void _EnterTree()
    {
        _gizmoPlugin = new SegmentedBodyGizmoPlugin();
        AddNode3DGizmoPlugin(_gizmoPlugin);
    }

    public override void _ExitTree()
    {
        if (_gizmoPlugin != null)
        {
            RemoveNode3DGizmoPlugin(_gizmoPlugin);
            _gizmoPlugin = null;
        }
    }
}
#endif