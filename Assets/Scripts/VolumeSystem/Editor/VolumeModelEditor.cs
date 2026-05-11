using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeModel))]
public class VolumeModelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        VolumeModel model = (VolumeModel)target;

        // =========================
        // PIPELINE
        // =========================

        EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        model.dataStructure = (VolumeDataStructure)EditorGUILayout.EnumPopup(
            "Data Structure",
            model.dataStructure
        );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(model, "Change Volume Pipeline");
            model.RebuildModel();
            EditorUtility.SetDirty(model);
        }

        GUILayout.Space(10);

        // =========================
        // OBJECT CREATION
        // =========================

        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        model.shapeToAdd = (VolumeShapeType)EditorGUILayout.EnumPopup(
            "Shape",
            model.shapeToAdd
        );

        model.roleToAdd = (VolumeOperationRole)EditorGUILayout.EnumPopup(
            "Role",
            model.roleToAdd
        );

        GUILayout.Space(5);

        // =========================
        // ADD / REMOVE
        // =========================

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Add Object", GUILayout.Height(30)))
        {
            Undo.RecordObject(model, "Add SDF Object");

            model.AddSelectedObject();

            EditorUtility.SetDirty(model);
        }

        if (GUILayout.Button("Remove Last", GUILayout.Height(30)))
        {
            Undo.RecordObject(model, "Remove Last SDF Object");

            model.RemoveLastObject();

            EditorUtility.SetDirty(model);
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        // =========================
        // CLEAR ALL
        // =========================

        Color oldColor = GUI.backgroundColor;

        GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);

        if (GUILayout.Button("Clear All Objects", GUILayout.Height(35)))
        {
            Undo.RecordObject(model, "Clear SDF Objects");

            model.ClearObjects();

            EditorUtility.SetDirty(model);
        }

        GUI.backgroundColor = oldColor;

        GUILayout.Space(10);

        // =========================
        // REBUILD
        // =========================

        if (GUILayout.Button("Rebuild Model", GUILayout.Height(30)))
        {
            model.RebuildModel();

            EditorUtility.SetDirty(model);
        }

        // =========================
        // DIRTY FLAG
        // =========================

        if (GUI.changed)
        {
            EditorUtility.SetDirty(model);
        }
    }
}