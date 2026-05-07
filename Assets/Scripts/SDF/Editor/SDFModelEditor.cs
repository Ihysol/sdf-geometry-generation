using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeModel))]
public class VolumeModelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        VolumeModel model = (VolumeModel)target;

        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        model.shapeToAdd = (VolumeShapeType)EditorGUILayout.EnumPopup("Shape", model.shapeToAdd);
        model.roleToAdd = (VolumeOperationRole)EditorGUILayout.EnumPopup("Role", model.roleToAdd);

        GUILayout.Space(5);

        if (GUILayout.Button("Add Object"))
        {
            Undo.RecordObject(model, "Add SDF Object");
            model.AddSelectedObject();
            EditorUtility.SetDirty(model);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Rebuild Model"))
        {
            model.RebuildModel();
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(model);
        }
    }
}