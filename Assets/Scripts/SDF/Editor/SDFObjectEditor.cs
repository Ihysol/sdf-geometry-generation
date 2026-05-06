using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SDFObject))]
public class SDFObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        Draw("shapeType");
        Draw("role");

        SDFShapeType shape = (SDFShapeType)serializedObject.FindProperty("shapeType").enumValueIndex;
        SDFGridType grid = (SDFGridType)serializedObject.FindProperty("gridType").enumValueIndex;

        EditorGUILayout.Space(8);

        switch (shape)
        {
            case SDFShapeType.CustomAsset:
                Header("Custom Asset");
                Draw("customAsset");
                break;

            case SDFShapeType.Sphere:
                Header("Sphere");
                Draw("sphereRadius");
                break;

            case SDFShapeType.Box:
                Header("Box");
                Draw("boxHalfExtents");
                break;

            case SDFShapeType.Torus:
                Header("Torus");
                Draw("torusMajorRadius");
                Draw("torusMinorRadius");
                break;

            case SDFShapeType.Hyperboloid:
                Header("Hyperboloid");
                Draw("hyperboloidA");
                Draw("hyperboloidB");
                Draw("hyperboloidC");
                break;
        }

        EditorGUILayout.Space(8);
        Header("Surface Grid / Cutter");
        Draw("gridType");

        grid = (SDFGridType)serializedObject.FindProperty("gridType").enumValueIndex;

        if (grid != SDFGridType.None)
        {
            Draw("gridWidth");
            Draw("gridDepth");
            Draw("gridOffset");

            switch (grid)
            {
                case SDFGridType.Global:
                    Draw("gridSpacing");
                    Draw("useXLines");
                    Draw("useYLines");
                    Draw("useZLines");
                    break;

                case SDFGridType.Sphere:
                    Draw("longitudeCount");
                    Draw("latitudeCount");
                    break;

                case SDFGridType.Torus:
                    Draw("torusMajorSegments");
                    Draw("torusMinorSegments");
                    break;

                case SDFGridType.Hyperboloid:
                    Draw("hyperboloidRadialSegments");
                    Draw("hyperboloidHeightSegments");
                    Draw("hyperboloidHeightMin");
                    Draw("hyperboloidHeightMax");
                    break;
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (GUI.changed)
        {
            EditorUtility.SetDirty(target);

            SDFObject obj = (SDFObject)target;
            var model = obj.GetComponentInParent<SDFModel>();

            if (model != null)
            {
                var sampler = model.GetComponent<SDFSampler>();
                if (sampler != null)
                    sampler.MarkDirty();
            }
        }
    }

    private void Draw(string propertyName)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop != null)
            EditorGUILayout.PropertyField(prop);
    }

    private void Header(string label)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }
}