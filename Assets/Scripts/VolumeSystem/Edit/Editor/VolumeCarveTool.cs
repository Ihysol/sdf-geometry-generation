using UnityEditor;
using UnityEngine;

/// <summary>ADR-004 Seam 1: Editor tool for carving regions into the volume via Scene View.
/// Click-drag to define carve region, applies CarveOperation to PersistentEditLayer.</summary>
[InitializeOnLoad]
public static class VolumeCarveTool
{
    private const string EnableKey = "_VolumeCarveToolEnabled";

    // Current carving state
    private static bool _carving;
    private static Vector3 _carveStartWorld;
    private static Vector3 _carveEndWorld;
    private static VolumeProcessor _targetProcessor;

    /// <summary>Brush radius in world units.</summary>
    public static float BrushRadius { get; set; } = 1f;

    static VolumeCarveTool()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void HandleEditUndoRedo()
    {
        if (!EditorPrefs.GetBool(EnableKey, false))
            return; // Carve tool inactive — skip edit undo/redo

        if (Event.current == null) return;
        if (Event.current.type != EventType.ValidateCommand || Event.current.commandName != "UndoRedoPerformed")
            return;

        var proc = Selection.activeGameObject?.GetComponent<VolumeProcessor>();
        if (proc?.EditLayer == null) return;

        // Unity fires ValidateCommand for both undo and redo — prefer undo when both available
        PersistentEditOperation changedOp = null;
        if (proc.EditLayer.CanUndo)
            changedOp = proc.EditLayer.Undo();
        else if (proc.EditLayer.CanRedo)
            changedOp = proc.EditLayer.Redo();

        if (changedOp != null)
        {
            Bounds bounds = changedOp.Region;
            proc.MarkDirtyBounds(bounds);
            Debug.Log($"[CarveTool] Edit Undo/Redo: {changedOp.Type}", proc.gameObject);
        }
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        // Handle Ctrl+Z / Ctrl+Y for edit operations (separate from CommandStack)
        HandleEditUndoRedo();

        bool enabled = EditorPrefs.GetBool(EnableKey, false);

        // Toggle toolbar button
        GUILayout.BeginArea(new Rect(10, 30, 120, 25));
        if (GUILayout.Toggle(enabled, "Carve Tool", EditorStyles.miniButtonLeft))
            EditorPrefs.SetBool(EnableKey, true);
        else
            EditorPrefs.SetBool(EnableKey, false);
        GUILayout.EndArea();

        if (!enabled) return;

        // Find selected VolumeProcessor
        var selected = Selection.activeGameObject?.GetComponent<VolumeProcessor>();
        if (selected == null)
        {
            Handles.Label(Camera.current != null ? Camera.current.transform.position + Vector3.forward * 5f : new Vector3(0, 2, 0),
                "Select a VolumeProcessor");
            return;
        }

        _targetProcessor = selected;

        // Brush size slider in toolbar
        GUILayout.BeginArea(new Rect(10, 60, 200, 25));
        BrushRadius = EditorGUILayout.Slider(BrushRadius, 0.1f, 5f);
        GUILayout.Label($"Brush: {BrushRadius:F1}", EditorStyles.miniLabel);
        GUILayout.EndArea();

        // Draw brush preview at mouse position
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (TryHitVolumeBounds(ray, _targetProcessor, out Vector3 hitPoint))
        {
            Handles.color = Color.red;
            Handles.DrawWireDisc(hitPoint, ray.direction, BrushRadius);

            // Carving interaction
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _carving = true;
                _carveStartWorld = hitPoint;
                _carveEndWorld = hitPoint;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && Event.current.button == 0 && _carving)
            {
                _carveEndWorld = hitPoint;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && _carving)
            {
                _carving = false;
                ApplyCarve(_carveStartWorld, _carveEndWorld);
                Event.current.Use();
            }

            // Draw carve region preview while dragging
            if (_carving)
            {
                Bounds carveBounds = GetCarveBounds(_carveStartWorld, _carveEndWorld);
                Handles.color = new Color(1f, 0f, 0f, 0.3f);
                Handles.DrawWireCube(carveBounds.center, carveBounds.size);
            }
        }
    }

    private static bool TryHitVolumeBounds(Ray ray, VolumeProcessor processor, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        if (processor.Pipeline == null)
            return false;

        var layout = processor.Pipeline.Buffer.Layout;
        Vector3 gridMin = layout.Origin;
        Vector3 gridMax = gridMin + (Vector3)layout.Resolution * layout.CellSize;

        Bounds gridBounds = new Bounds((gridMin + gridMax) * 0.5f, (gridMax - gridMin));

        // Transform to world space
        gridBounds.center = processor.transform.TransformPoint(gridBounds.center);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            // Check if hit is within volume bounds
            Vector3 localHit = processor.transform.InverseTransformPoint(hit.point);
            if (localHit.x >= gridMin.x && localHit.x <= gridMax.x &&
                localHit.y >= gridMin.y && localHit.y <= gridMax.y &&
                localHit.z >= gridMin.z && localHit.z <= gridMax.z)
            {
                hitPoint = hit.point;
                return true;
            }
        }

        // Fallback: intersect ray with volume bounding box
        if (SphereCastFromRay(ray, gridBounds, out hitPoint))
            return true;

        return false;
    }

    private static bool SphereCastFromRay(Ray ray, Bounds bounds, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        // Simple AABB intersection with ray
        float tmin = -Mathf.Infinity;
        float tmax = Mathf.Infinity;

        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(ray.direction[i]) < 1e-6f)
            {
                if (ray.origin[i] < bounds.min[i] || ray.origin[i] > bounds.max[i])
                    return false;
            }
            else
            {
                float t1 = (bounds.min[i] - ray.origin[i]) / ray.direction[i];
                float t2 = (bounds.max[i] - ray.origin[i]) / ray.direction[i];
                tmin = Mathf.Max(tmin, Mathf.Min(t1, t2));
                tmax = Mathf.Min(tmax, Mathf.Max(t1, t2));
            }
        }

        if (tmax < tmin || tmax < 0)
            return false;

        hitPoint = ray.GetPoint(tmin > 0 ? tmin : tmax);
        return true;
    }

    private static Bounds GetCarveBounds(Vector3 start, Vector3 end)
    {
        Vector3 center = (start + end) * 0.5f;
        Vector3 size = Vector3.Max(new Vector3(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y), Mathf.Abs(end.z - start.z)), new Vector3(BrushRadius * 2f, BrushRadius * 2f, BrushRadius * 2f));
        return new Bounds(center, size);
    }

    private static void ApplyCarve(Vector3 start, Vector3 end)
    {
        if (_targetProcessor == null || _targetProcessor.EditLayer == null)
            return;

        Bounds carveBounds = GetCarveBounds(start, end);

        // Create world-anchored carve operation
        var op = new CarveOperation(
            carveBounds,
            new EditAnchor { type = EditAnchorType.World },
            depth: 1.0f
        );

        _targetProcessor.EditLayer.Add(op);

        // Trigger rebuild to apply the carve
        _targetProcessor.MarkDirtyBounds(carveBounds);

        Debug.Log($"[CarveTool] Carved region: {carveBounds.center:F2} size:{carveBounds.size:F2}", _targetProcessor.gameObject);
    }
}
