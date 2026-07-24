using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeObjectRegistry))]
public class VolumeObjectRegistryEditor : Editor
{
    /// <summary>Draws composer object-list and maintenance controls.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeObjectRegistry composer = (VolumeObjectRegistry)target;

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
