using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SDFModel))]
public class SDFModelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SDFModel model = (SDFModel)target;

        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        model.shapeToAdd = (SDFShapeType)EditorGUILayout.EnumPopup("Shape", model.shapeToAdd);
        model.roleToAdd = (SDFOperationRole)EditorGUILayout.EnumPopup("Role", model.roleToAdd);

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