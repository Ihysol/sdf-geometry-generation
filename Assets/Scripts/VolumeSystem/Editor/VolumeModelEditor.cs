using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeModel))]
public class VolumeModelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeModel model = (VolumeModel)target;

        EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);

        SerializedProperty dataStructureProp =
            serializedObject.FindProperty("dataStructure");

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(dataStructureProp, new GUIContent("Data Structure"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Change Volume Pipeline");
            model.RebuildModel();
            EditorUtility.SetDirty(model);

            serializedObject.Update();
        }

        GUILayout.Space(10);

        DrawActiveSamplerSettings(model);

        GUILayout.Space(10);

        EditorGUILayout.LabelField("Meshing", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("isoLevel"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("recalculateNormals"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("recalculateBounds"));

        if (model.dataStructure == VolumeDataStructure.Octree)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("renderOctreeDebugCubes"));
        }

        GUILayout.Space(10);

        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("shapeToAdd"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("roleToAdd"));

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Add Object", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Add SDF Object");
            model.AddSelectedObject();
            EditorUtility.SetDirty(model);
        }

        if (GUILayout.Button("Remove Last", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Remove Last SDF Object");
            model.RemoveLastObject();
            EditorUtility.SetDirty(model);
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);

        if (GUILayout.Button("Clear All Objects", GUILayout.Height(35)))
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Clear SDF Objects");
            model.ClearObjects();
            EditorUtility.SetDirty(model);
        }

        GUI.backgroundColor = oldColor;

        GUILayout.Space(10);

        if (GUILayout.Button("Rebuild Model", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();

            model.RebuildModel();
            EditorUtility.SetDirty(model);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawActiveSamplerSettings(VolumeModel model)
    {
        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    EditorGUILayout.LabelField("Voxel Grid", EditorStyles.boldLabel);

                    SerializedProperty samplerProp =
                        serializedObject.FindProperty("voxelGridSampler");

                    SerializedProperty builderProp =
                        samplerProp.FindPropertyRelative("builder");

                    EditorGUILayout.PropertyField(
                        builderProp.FindPropertyRelative("uniformExtent")
                    );

                    EditorGUILayout.PropertyField(
                        builderProp.FindPropertyRelative("gridExtent")
                    );

                    EditorGUILayout.PropertyField(
                        builderProp.FindPropertyRelative("uniformResolution")
                    );

                    EditorGUILayout.PropertyField(
                        builderProp.FindPropertyRelative("gridSize")
                    );

                    break;
                }

            case VolumeDataStructure.Octree:
                {
                    EditorGUILayout.LabelField("Octree", EditorStyles.boldLabel);

                    SerializedProperty samplerProp =
                        serializedObject.FindProperty("octreeSampler");

                    SerializedProperty builderProp =
                        samplerProp.FindPropertyRelative("builder");

                    EditorGUILayout.PropertyField(builderProp, true);

                    break;
                }
        }
    }
}