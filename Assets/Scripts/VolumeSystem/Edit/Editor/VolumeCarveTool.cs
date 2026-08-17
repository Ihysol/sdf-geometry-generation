using UnityEditor;
using UnityEngine;

/// <summary>ADR-004 Seam 1: Editor tool for carving regions into the volume via Scene View.
/// Toggle via Tools > SDF Carve Tool (or assign a shortcut). Click-drag on the red disc to carve.</summary>
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

    public static bool IsEnabled => EditorPrefs.GetBool(EnableKey, false);

    public static bool IsCarving => _carving;

    // ---------- Menu toggle (assignable to a shortcut in Unity prefs) ----------

    [MenuItem("Tools/SDF Carve Tool", false, 20)]
    private static void ToggleCarveTool()
    {
        SetEnabled(!IsEnabled);
        RepaintMenu();
    }

    [MenuItem("Tools/SDF Carve Tool", true)]
    private static bool ValidateToggle()
    {
        Menu.SetChecked("Tools/SDF Carve Tool", IsEnabled);
        return true;
    }

    // Keep the checkmark in sync every time the menu opens
    [InitializeOnLoadMethod]
    private static void RepaintMenu()
    {
        Menu.SetChecked("Tools/SDF Carve Tool", IsEnabled);
    }

    // ---------- Enable / disable (also callable from other scripts) ----------

    public static void SetEnabled(bool enabled)
    {
        EditorPrefs.SetBool(EnableKey, enabled);
        if (!enabled)
        {
            _carving = false;
            _targetProcessor = null;
        }
        SceneView.RepaintAll();
        RepaintMenu();
    }

    // ---------- Scene GUI (brush preview + carving interaction) ----------

    static VolumeCarveTool()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void HandleEditUndoRedo()
    {
        if (!IsEnabled)
            return;

        if (Event.current == null) return;
        if (Event.current.type != EventType.ValidateCommand || Event.current.commandName != "UndoRedoPerformed")
            return;

        var proc = Selection.activeGameObject?.GetComponent<VolumeProcessor>();
        if (proc?.EditLayer == null) return;

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

    /// <summary>Check if a mouse click is close enough to our brush disc center in screen space.</summary>
    private static bool IsClickOnBrushDisc(Vector3 hitPoint, Camera cam)
    {
        Vector3 discScreenPos = cam.WorldToScreenPoint(hitPoint);
        Vector2 mousePos2D = new Vector2(Event.current.mousePosition.x, Event.current.mousePosition.y);
        float screenDist = Vector2.Distance(mousePos2D, new Vector2(discScreenPos.x, discScreenPos.y));

        // Project one brush-radius into screen space to get the hit threshold
        Vector3 tangent = Vector3.Cross(cam.transform.forward, Vector3.up);
        if (tangent.sqrMagnitude < 0.001f)
            tangent = Vector3.Cross(cam.transform.forward, Vector3.right);
        tangent.Normalize();
        Vector3 edgePoint = hitPoint + tangent * BrushRadius;
        Vector3 edgeScreen = cam.WorldToScreenPoint(edgePoint);
        float screenRadius = Vector2.Distance(new Vector2(discScreenPos.x, discScreenPos.y),
                                              new Vector2(edgeScreen.x, edgeScreen.y));

        return screenDist <= screenRadius + 5f; // +5px tolerance
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        // Edit undo/redo (Ctrl+Z / Ctrl+Y on the edit layer)
        HandleEditUndoRedo();

        if (!IsEnabled) return;

        // Find selected VolumeProcessor
        var selected = Selection.activeGameObject?.GetComponent<VolumeProcessor>();
        if (selected == null)
            return;

        _targetProcessor = selected;

        // Draw brush preview at mouse position over the volume
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (TryHitVolumeBounds(ray, _targetProcessor, out Vector3 hitPoint))
        {
            Handles.color = Color.red;
            Handles.DrawWireDisc(hitPoint, ray.direction, BrushRadius);

            // Carving interaction — ONLY capture MouseDown when the click is visually ON our brush disc.
            // Clicks outside the disc (e.g., on Unity transform gimbal handles) pass through untouched.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Camera.current != null
                && IsClickOnBrushDisc(hitPoint, Camera.current))
            {
                _carving = true;
                _carveStartWorld = hitPoint;
                _carveEndWorld = hitPoint;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && Event.current.button == 0 && _carving)
            {
                if (TryHitVolumeBounds(ray, _targetProcessor, out Vector3 dragHit))
                    _carveEndWorld = dragHit;
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

    // ---------- Hit testing helpers ----------

    private static bool TryHitVolumeBounds(Ray ray, VolumeProcessor processor, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        if (processor.Pipeline == null)
            return false;

        var layout = processor.Pipeline.Buffer.Layout;
        Vector3 gridMin = layout.Origin;
        Vector3 gridMax = gridMin + (Vector3)layout.Resolution * layout.CellSize;

        Bounds gridBounds = new Bounds((gridMin + gridMax) * 0.5f, (gridMax - gridMin));
        gridBounds.center = processor.transform.TransformPoint(gridBounds.center);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            Vector3 localHit = processor.transform.InverseTransformPoint(hit.point);
            if (localHit.x >= gridMin.x && localHit.x <= gridMax.x &&
                localHit.y >= gridMin.y && localHit.y <= gridMax.y &&
                localHit.z >= gridMin.z && localHit.z <= gridMax.z)
            {
                hitPoint = hit.point;
                return true;
            }
        }

        if (SphereCastFromRay(ray, gridBounds, out hitPoint))
            return true;

        return false;
    }

    private static bool SphereCastFromRay(Ray ray, Bounds bounds, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
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
        Vector3 size = Vector3.Max(
            new Vector3(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y), Mathf.Abs(end.z - start.z)),
            new Vector3(BrushRadius * 2f, BrushRadius * 2f, BrushRadius * 2f));
        return new Bounds(center, size);
    }

    private static void ApplyCarve(Vector3 start, Vector3 end)
    {
        if (_targetProcessor == null || _targetProcessor.EditLayer == null)
            return;

        Bounds carveBounds = GetCarveBounds(start, end);
        var op = new CarveOperation(
            carveBounds,
            new EditAnchor { type = EditAnchorType.World },
            depth: 1.0f);

        _targetProcessor.EditLayer.Add(op);
        _targetProcessor.MarkDirtyBounds(carveBounds);
        Debug.Log($"[CarveTool] Carved region: {carveBounds.center:F2} size:{carveBounds.size:F2}", _targetProcessor.gameObject);
    }
}
