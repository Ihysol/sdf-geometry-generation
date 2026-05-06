using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SDFSceneComposer))]
public class SDFSceneComposerEditor : Editor
{
    private ReorderableList _list;

    private void OnEnable()
    {
        SerializedProperty objectsProp = serializedObject.FindProperty("objects");

        _list = new ReorderableList(serializedObject, objectsProp, true, true, true, true);

        _list.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "SDF Objects");
        };

        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            SerializedProperty element = objectsProp.GetArrayElementAtIndex(index);
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.PropertyField(rect, element, GUIContent.none);
        };

        // + Button
        _list.onAddCallback = list =>
        {
            SDFSceneComposer composer = (SDFSceneComposer)target;
            SDFObject obj = CreateDefaultSphere(composer);

            objectsProp.arraySize++;
            objectsProp.GetArrayElementAtIndex(objectsProp.arraySize - 1).objectReferenceValue = obj;

            serializedObject.ApplyModifiedProperties();

            Rebuild(composer);
        };

        // - Button
        _list.onRemoveCallback = list =>
        {
            SDFSceneComposer composer = (SDFSceneComposer)target;

            int index = list.index;

            if (index >= 0 && index < composer.objects.Count)
            {
                SDFObject obj = composer.objects[index];

                objectsProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();

                if (obj != null)
                    Undo.DestroyObjectImmediate(obj.gameObject);

                Rebuild(composer);
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        _list.DoLayoutList();

        serializedObject.ApplyModifiedProperties();

        SDFSceneComposer composer = (SDFSceneComposer)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Add Default Sphere"))
        {
            SDFObject obj = CreateDefaultSphere(composer);

            Undo.RecordObject(composer, "Add SDF Object");
            composer.objects.Add(obj);

            EditorUtility.SetDirty(composer);
            Rebuild(composer);
        }

        if (GUILayout.Button("Clear All SDF Objects"))
        {
            ClearAll(composer);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Rebuild Composition"))
        {
            Rebuild(composer);
        }
    }

    private static SDFObject CreateDefaultSphere(SDFSceneComposer composer)
    {
        GameObject child = new GameObject("SDFObject_Sphere_Add");
        Undo.RegisterCreatedObjectUndo(child, "Create SDF Object");

        child.transform.SetParent(composer.transform, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;

        SDFObject sdfObject = child.AddComponent<SDFObject>();
        sdfObject.shapeType = SDFShapeType.Sphere;
        sdfObject.role = SDFOperationRole.Add;

        return sdfObject;
    }

    private static void ClearAll(SDFSceneComposer composer)
    {
        Undo.RecordObject(composer, "Clear SDF Objects");

        for (int i = composer.objects.Count - 1; i >= 0; i--)
        {
            SDFObject obj = composer.objects[i];

            if (obj != null)
                Undo.DestroyObjectImmediate(obj.gameObject);
        }

        composer.objects.Clear();

        EditorUtility.SetDirty(composer);
        Rebuild(composer);
    }

    private static void Rebuild(SDFSceneComposer composer)
    {
        SDFModel model = composer.GetComponent<SDFModel>();

        if (model != null)
        {
            model.RebuildModel();
            return;
        }

        composer.RebuildComposition();

        SDFDualContouringRenderer renderer = composer.GetComponent<SDFDualContouringRenderer>();
        if (renderer != null)
            renderer.RebuildMesh();
    }
}