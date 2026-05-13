using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeObject))]
public class VolumeObjectEditor : Editor
{
    /// <summary>Draws the shape-specific inspector for a volume object.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();

        Draw("shapeType");
        Draw("role");

        VolumeShapeType shape =
            (VolumeShapeType)serializedObject.FindProperty("shapeType").enumValueIndex;

        EditorGUILayout.Space(8);

        switch (shape)
        {
            case VolumeShapeType.CustomAsset:
                Header("Custom Asset");
                Draw("customAsset");
                break;

            case VolumeShapeType.Sphere:
                Header("Sphere");
                Draw("sphereRadius");
                break;

            case VolumeShapeType.Box:
                Header("Box");
                Draw("boxHalfExtents");
                break;

            case VolumeShapeType.Torus:
                Header("Torus");
                Draw("torusMajorRadius");
                Draw("torusMinorRadius");
                break;

            case VolumeShapeType.Hyperboloid:
                Header("Hyperboloid");
                Draw("hyperboloidA");
                Draw("hyperboloidB");
                Draw("hyperboloidC");
                break;
        }

        EditorGUILayout.Space(8);

        Header("Surface Grid / Cutter");
        Draw("gridType");

        VolumeGridType grid =
            (VolumeGridType)serializedObject.FindProperty("gridType").enumValueIndex;

        if (grid != VolumeGridType.None)
        {
            Draw("gridWidth");
            Draw("gridDepth");
            Draw("autoClampGridToSampling");
            Draw("gridOffset");

            switch (grid)
            {
                case VolumeGridType.Global:
                    Draw("globalGridInWorldSpace");
                    Draw("gridSpacing");
                    Draw("useXLines");
                    Draw("useYLines");
                    Draw("useZLines");
                    break;

                case VolumeGridType.Sphere:
                    Draw("longitudeCount");
                    Draw("latitudeCount");
                    break;

                case VolumeGridType.Torus:
                    Draw("torusMajorSegments");
                    Draw("torusMinorSegments");
                    break;

                case VolumeGridType.Hyperboloid:
                    Draw("hyperboloidRadialSegments");
                    Draw("hyperboloidHeightSegments");
                    Draw("hyperboloidHeightMin");
                    Draw("hyperboloidHeightMax");
                    break;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            VolumeObject obj = (VolumeObject)target;

            EditorUtility.SetDirty(obj);

            VolumeModel model = obj.GetComponentInParent<VolumeModel>();

            if (model != null && model.autoRebuildOnChange)
            {
                model.RebuildModel();
                EditorUtility.SetDirty(model);
            }
        }
        else
        {
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>Draws a serialized property when it exists.</summary>
    private void Draw(string propertyName)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);

        if (prop != null)
            EditorGUILayout.PropertyField(prop);
    }

    /// <summary>Draws a bold section header.</summary>
    private void Header(string label)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }
}
