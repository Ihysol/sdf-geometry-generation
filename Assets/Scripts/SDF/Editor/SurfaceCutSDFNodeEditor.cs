using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SurfaceCutSDF))]
public class SurfaceCutSDFNodeEditor : Editor
{
    private Editor _baseEditor;
    private Editor _cutterEditor;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var baseProp = serializedObject.FindProperty("baseShape");
        var cutterProp = serializedObject.FindProperty("cutter");

        EditorGUILayout.PropertyField(baseProp);
        EditorGUILayout.PropertyField(cutterProp);

        serializedObject.ApplyModifiedProperties();

        var node = (SurfaceCutSDF)target;

        // Base Shape Inspector
        if (node.baseShape != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Base Shape", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            CreateCachedEditor(node.baseShape, null, ref _baseEditor);
            _baseEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }

        // Cutter Inspector
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