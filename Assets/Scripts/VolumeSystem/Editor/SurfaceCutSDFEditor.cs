using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SurfaceCutSDF))]
public class SurfaceCutSDFEditor : Editor
{
    private Editor _baseEditor;
    private Editor _cutterEditor;

    /// <summary>Draws nested inspectors for the base shape and cutter assets.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty baseProp = serializedObject.FindProperty("baseShape");
        SerializedProperty cutterProp = serializedObject.FindProperty("cutter");

        EditorGUILayout.PropertyField(baseProp);
        EditorGUILayout.PropertyField(cutterProp);

        serializedObject.ApplyModifiedProperties();

        SurfaceCutSDF node = (SurfaceCutSDF)target;

        if (node.baseShape != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Base Shape", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            CreateCachedEditor(node.baseShape, null, ref _baseEditor);
            _baseEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }

        if (node.cutter != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Cutter", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            CreateCachedEditor(node.cutter, null, ref _cutterEditor);
            _cutterEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }
    }
}
