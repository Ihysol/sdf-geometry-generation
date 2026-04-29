using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SDFSampler))]
public class SDFSamplerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SDFSampler sampler = (SDFSampler)target;

        serializedObject.Update();

        // ===== Shape selector =====
        EditorGUILayout.PropertyField(serializedObject.FindProperty("shapeMode"));

        var mode = sampler.shapeMode;

        EditorGUILayout.Space();

        // ===== Shape specific =====
        if (mode == SDFSampler.ShapeMode.Sphere)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sphereRadius"));
        }
        else if (mode == SDFSampler.ShapeMode.Box)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("boxHalfExtents"));
        }
        else if (mode == SDFSampler.ShapeMode.Torus)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("torusMajorRadius"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("torusMinorRadius"));
        }
        else if (mode == SDFSampler.ShapeMode.Hyperboloid)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hyperboloidA"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hyperboloidB"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hyperboloidC"));
        }

        // =========================
        // GRID BLOCK
        // =========================

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Surface Grid", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("useGrid"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("gridMode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("invertGrid"));

        if (sampler.useGrid)
        {
            var gridMode = sampler.gridMode;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("gridGrooveWidth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gridGrooveDepth"));

            if (gridMode == SDFSampler.GridMode.SphereGrid ||
                gridMode == SDFSampler.GridMode.TorusGrid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gridLongitudeCount"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gridLatitudeCount"));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gridSpacing"));
            }
        }

        // =========================
        // Sampling Grid
        // =========================

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sampling Grid", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("useAutomaticBounds"));

        if (!sampler.useAutomaticBounds)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gridExtent"));
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty("boundsPadding"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("resolution"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("uniformResolution"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("targetFPS"));
        serializedObject.ApplyModifiedProperties();
    }
}