using UnityEditor;
using UnityEngine;

/// <summary>Editor for VolumeObject — tracks transform changes and shape param edits, pushes to CommandStack.</summary>
[CustomEditor(typeof(VolumeObject))]
public class VolumeObjectEditor : Editor
{
    private SerializedProperty _sphereRadius;
    private SerializedProperty _boxHalfExtents;
    private SerializedProperty _torusMajorRadius;
    private SerializedProperty _torusMinorRadius;
    private SerializedProperty _hyperboloidA;
    private SerializedProperty _hyperboloidB;
    private SerializedProperty _hyperboloidC;
    private SerializedProperty _shapeType;
    private SerializedProperty _role;
    private SerializedProperty _gridType;

    /// <summary>Snapshot at start of drag — pushed to CommandStack when drag ends.</summary>
    private Vector3? _dragStartPos;
    private Quaternion? _dragStartRot;
    private Vector3? _dragStartScl;
    private bool _trackingTransform;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Init serialized refs once
        if (_shapeType == null)
        {
            _shapeType = serializedObject.FindProperty("shapeType");
            _role = serializedObject.FindProperty("role");
            _sphereRadius = serializedObject.FindProperty("sphereRadius");
            _boxHalfExtents = serializedObject.FindProperty("boxHalfExtents");
            _torusMajorRadius = serializedObject.FindProperty("torusMajorRadius");
            _torusMinorRadius = serializedObject.FindProperty("torusMinorRadius");
            _hyperboloidA = serializedObject.FindProperty("hyperboloidA");
            _hyperboloidB = serializedObject.FindProperty("hyperboloidB");
            _hyperboloidC = serializedObject.FindProperty("hyperboloidC");
            _gridType = serializedObject.FindProperty("gridType");
        }

        EditorGUI.BeginChangeCheck();

        // Core properties
        EditorGUILayout.PropertyField(_shapeType, new GUIContent("Shape"));
        EditorGUILayout.PropertyField(_role, new GUIContent("Role"));

        VolumeShapeType shape = (VolumeShapeType)_shapeType.enumValueIndex;
        EditorGUILayout.Space(6);

        // Shape-specific params
        switch (shape)
        {
            case VolumeShapeType.Sphere:
                Header("Sphere");
                DrawIfValid(_sphereRadius, "Radius");
                break;
            case VolumeShapeType.Box:
                Header("Box");
                DrawIfValid(_boxHalfExtents, "Half Extents");
                break;
            case VolumeShapeType.Torus:
                Header("Torus");
                DrawIfValid(_torusMajorRadius, "Major Radius");
                DrawIfValid(_torusMinorRadius, "Minor Radius");
                break;
            case VolumeShapeType.Hyperboloid:
                Header("Hyperboloid");
                DrawIfValid(_hyperboloidA, "A");
                DrawIfValid(_hyperboloidB, "B");
                DrawIfValid(_hyperboloidC, "C");
                break;
        }

        EditorGUILayout.Space(6);
        Header("Grid / Cutter");
        DrawIfValid(_gridType, "Grid Type");

        if (_gridType != null && (VolumeGridType)_gridType.enumValueIndex != VolumeGridType.None)
        {
            EditorGUI.indentLevel++;
            DrawIfValid(serializedObject.FindProperty("gridWidth"));
            DrawIfValid(serializedObject.FindProperty("gridDepth"));
            DrawIfValid(serializedObject.FindProperty("autoClampGridToSampling"));
            DrawIfValid(serializedObject.FindProperty("gridOffset"));

            VolumeGridType grid = (VolumeGridType)_gridType.enumValueIndex;
            switch (grid)
            {
                case VolumeGridType.Global:
                    DrawIfValid(serializedObject.FindProperty("globalGridInWorldSpace"));
                    DrawIfValid(serializedObject.FindProperty("gridSpacing"));
                    DrawIfValid(serializedObject.FindProperty("useXLines"));
                    DrawIfValid(serializedObject.FindProperty("useYLines"));
                    DrawIfValid(serializedObject.FindProperty("useZLines"));
                    break;
                case VolumeGridType.Sphere:
                    DrawIfValid(serializedObject.FindProperty("longitudeCount"));
                    DrawIfValid(serializedObject.FindProperty("latitudeCount"));
                    break;
                case VolumeGridType.Torus:
                    DrawIfValid(serializedObject.FindProperty("torusMajorSegments"));
                    DrawIfValid(serializedObject.FindProperty("torusMinorSegments"));
                    break;
                case VolumeGridType.Hyperboloid:
                    DrawIfValid(serializedObject.FindProperty("hyperboloidRadialSegments"));
                    DrawIfValid(serializedObject.FindProperty("hyperboloidHeightSegments"));
                    DrawIfValid(serializedObject.FindProperty("hyperboloidHeightMin"));
                    DrawIfValid(serializedObject.FindProperty("hyperboloidHeightMax"));
                    break;
            }
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);

            VolumeProcessor proc = ((VolumeObject)target).GetComponentInParent<VolumeProcessor>();
            if (proc != null && proc.ShouldAutoRebuildOnChange())
            {
                proc.RebuildModel();
                EditorUtility.SetDirty(proc);
            }
        }
        else
        {
            serializedObject.ApplyModifiedProperties();
        }

        // Detect drag start via SceneView
        TrackTransformChanges();
    }

    private void OnEnable() => StartTracking();
    private void OnDisable() => StopTracking();

    private void StartTracking()
    {
        VolumeObject vo = target as VolumeObject;
        if (vo == null) return;

        _dragStartPos = vo.transform.localPosition;
        _dragStartRot = vo.transform.localRotation;
        _dragStartScl = vo.transform.localScale;
        _trackingTransform = true;

        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void StopTracking()
    {
        _trackingTransform = false;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    /// <summary>Push TransformCommand once per drag (on mouse release), not per frame.</summary>
    private void OnSceneGUI(SceneView sv)
    {
        if (!_trackingTransform) return;

        VolumeObject vo = target as VolumeObject;
        if (!vo)
        {
            StopTracking();
            return;
        }

        Transform t = vo.transform;

        // Detect drag end: mouse up after transform changed
        if (Event.current.type == EventType.MouseUp && Selection.activeTransform == t)
        {
            Vector3 oldPos = _dragStartPos ?? t.localPosition;
            Quaternion oldRot = _dragStartRot ?? t.localRotation;
            Vector3 oldScl = _dragStartScl ?? t.localScale;

            // Only push if something actually changed
            if (t.localPosition != oldPos || t.localRotation != oldRot || t.localScale != oldScl)
            {
                Bounds bounds = vo.GetEstimatedLocalBounds();
                Vector3 center = t.TransformPoint(bounds.center);
                float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 1.5f;
                Bounds affected = new Bounds(center, Vector3.one * extent);

                VolumeProcessor proc = vo.GetComponentInParent<VolumeProcessor>();
                if (proc != null)
                {
                    proc.CommandStack.Push(new TransformCommand(
                        t, oldPos, oldRot, oldScl,
                        t.localPosition, t.localRotation, t.localScale, affected));
                }
            }

            // Reset tracking snapshot to current state
            _dragStartPos = t.localPosition;
            _dragStartRot = t.localRotation;
            _dragStartScl = t.localScale;
        }
    }

    private void TrackTransformChanges()
    {
        // Inspector changes don't affect transforms — handled by OnSceneGUI
    }

    private void DrawIfValid(SerializedProperty prop, string label = null)
    {
        if (prop != null)
            EditorGUILayout.PropertyField(prop, new GUIContent(label ?? prop.displayName));
    }

    private void Header(string label) => EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
}
