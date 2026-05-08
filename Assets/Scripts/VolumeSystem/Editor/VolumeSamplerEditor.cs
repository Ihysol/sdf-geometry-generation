using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeSampler))]
public class VolumeSamplerEditor : Editor
{
    private Editor _sdfEditor;

    public override void OnInspectorGUI()
    {
        VolumeSampler sampler = (VolumeSampler)target;

        serializedObject.Update();

        // =========================
        // SDF SLOT
        // =========================
        EditorGUILayout.PropertyField(serializedObject.FindProperty("sdf"));

        serializedObject.ApplyModifiedProperties();

        // =========================
        // SDF INLINE INSPECTOR
        // =========================
        if (sampler.sdf != null)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("SDF Asset Parameters", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            CreateCachedEditor(sampler.sdf, null, ref _sdfEditor);
            _sdfEditor.OnInspectorGUI();

            EditorGUI.indentLevel--;
        }

        serializedObject.Update();

        // =========================
        // VOLUME SETTINGS
        // =========================
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Volume", EditorStyles.boldLabel);

        SerializedProperty builderProp = serializedObject.FindProperty("builder");

        EditorGUILayout.PropertyField(serializedObject.FindProperty("uniformExtent"));
        EditorGUILayout.PropertyField(builderProp.FindPropertyRelative("gridExtent"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("uniformResolution"));
        EditorGUILayout.PropertyField(builderProp.FindPropertyRelative("gridSize"));;

        serializedObject.ApplyModifiedProperties();

        // =========================
        // RENDERER SETTINGS
        // =========================
        var renderer = sampler.GetComponent<VolumeMeshRenderer>();

        if (renderer != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Mesh Rebuild", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            bool newAuto = EditorGUILayout.Toggle(
                "Auto Rebuild On Change",
                renderer.autoRebuildOnChange
            );

            bool newRealtime = EditorGUILayout.Toggle(
                "Rebuild Every Frame",
                renderer.rebuildEveryFrame
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(renderer, "Change Rebuild Settings");

                renderer.autoRebuildOnChange = newAuto;
                renderer.rebuildEveryFrame = newRealtime;

                EditorUtility.SetDirty(renderer);
            }
        }

        // =========================
        // ACTION BUTTONS
        // =========================
        EditorGUILayout.Space(10);

        if (GUILayout.Button("Rebuild Mesh"))
        {
            sampler.MarkDirty();

            if (renderer != null)
                renderer.RebuildMesh();
            else
                sampler.RebuildVolume();
        }

        if (GUILayout.Button("Destroy Mesh"))
        {
            if (renderer != null)
                renderer.DestroyMesh();
        }
    }
}