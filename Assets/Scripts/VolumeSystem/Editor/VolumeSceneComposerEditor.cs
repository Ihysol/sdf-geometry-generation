using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeSceneComposer))]
public class VolumeSceneComposerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeSceneComposer composer = (VolumeSceneComposer)target;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("objects"),
            true
        );

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Rebuild Composition"))
        {
            composer.RebuildComposition();

            EditorUtility.SetDirty(composer);
        }

        if (GUILayout.Button("Clear All"))
        {
            Undo.RegisterFullObjectHierarchyUndo(
                composer.gameObject,
                "Clear Volume Objects"
            );

            for (int i = composer.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = composer.transform.GetChild(i);

#if UNITY_EDITOR
                DestroyImmediate(child.gameObject);
#else
                Destroy(child.gameObject);
#endif
            }

            composer.objects.Clear();

            EditorUtility.SetDirty(composer);
        }

        serializedObject.ApplyModifiedProperties();
    }
}