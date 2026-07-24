using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeProcessor))]
public class VolumeProcessorEditor : Editor
{
    private bool _showPipeline = true;
    private bool _showObjects = true;
    private bool _showDebug = false;

    // Operation params (not serialized on model)
    private Vector3 _opCenter = Vector3.zero;
    private float _opRadius = 1f;
    private int _opMaterialId = 0;
    private int _opSmoothIterations = 1;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeProcessor model = (VolumeProcessor)target;
        EditorGUI.BeginChangeCheck();

        DrawPipelineSection(model);
        EditorGUILayout.Space(10);
        DrawObjectsSection(model);
        EditorGUILayout.Space(10);
        DrawDebugSection(model);
        EditorGUILayout.Space(10);
        DrawRebuildButton(model);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(model);
            model.RebuildModel();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPipelineSection(VolumeProcessor model)
    {
        _showPipeline = EditorGUILayout.Foldout(_showPipeline, "Pipeline", true);
        if (!_showPipeline) return;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(SafeProp("surfaceMaterial"), new GUIContent("Surface Material"));

        model.enablePipeline = EditorGUILayout.Toggle("Enable Pipeline", model.enablePipeline);

        if (!model.enablePipeline)
        {
            EditorGUI.EndChangeCheck();
            return;
        }

        EditorGUI.indentLevel++;

        model.pipelineMesherType = (PipelineMesherType)EditorGUILayout.EnumPopup("Mesher Type", model.pipelineMesherType);
        model.computeBackend = (ComputeBackend)EditorGUILayout.EnumPopup("Compute Backend", model.computeBackend);
        model.resolution = EditorGUILayout.Vector3IntField("Resolution", model.resolution);
        model.chunkSize = EditorGUILayout.IntField("Chunk Size", Mathf.Max(1, model.chunkSize));
        model.boundsExtent = EditorGUILayout.FloatField("Bounds Extent", Mathf.Max(0.1f, model.boundsExtent));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Meshing", EditorStyles.boldLabel);
        model.isoLevel = EditorGUILayout.Slider("Iso Level", model.isoLevel, -1f, 1f);

        EditorGUI.indentLevel--;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        _opCenter = EditorGUILayout.Vector3Field("Center", _opCenter);
        _opRadius = EditorGUILayout.FloatField("Radius", Mathf.Max(0.01f, _opRadius));
        _opMaterialId = EditorGUILayout.IntField("Material ID", _opMaterialId);

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Sphere"))
            ExecuteOp(model, new AddSphereOperation(_opCenter, _opRadius, _opMaterialId));
        if (GUILayout.Button("Subtract Sphere"))
            ExecuteOp(model, new SubtractSphereOperation(_opCenter, _opRadius));
        EditorGUILayout.EndHorizontal();

        _opSmoothIterations = EditorGUILayout.IntField("Smooth Iterations", Mathf.Max(1, _opSmoothIterations));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Smooth"))
            ExecuteOp(model, new SmoothOperation(_opCenter, _opRadius, _opSmoothIterations));
        if (GUILayout.Button("Paint Material"))
            ExecuteOp(model, new PaintMaterialOperation(_opCenter, _opRadius, _opMaterialId));
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(model);
            model.RebuildModel();
        }
    }

    private void ExecuteOp(VolumeProcessor model, IVolumeOperation op)
    {
        if (!model.enablePipeline || !model.Initialized)
            model.Initialize();
        model.ExecuteOperation(op);
        model.RebuildModel();
        EditorUtility.SetDirty(model);
    }

    private void DrawObjectsSection(VolumeProcessor model)
    {
        _showObjects = EditorGUILayout.Foldout(_showObjects, "Objects", true);
        if (!_showObjects) return;

        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        model.shapeToAdd = (VolumeShapeType)EditorGUILayout.EnumPopup("Shape", model.shapeToAdd);
        model.roleToAdd = (VolumeOperationRole)EditorGUILayout.EnumPopup("Role", model.roleToAdd);

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Object", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(model, "Add SDF Object");
            model.AddSelectedObject();
            EditorUtility.SetDirty(model);
        }
        if (GUILayout.Button("Remove Last", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(model, "Remove Last SDF Object");
            model.RemoveLastObject();
            EditorUtility.SetDirty(model);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
        if (GUILayout.Button("Clear All Objects", GUILayout.Height(35)))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(model, "Clear SDF Objects");
            model.ClearObjects();
            EditorUtility.SetDirty(model);
        }
        GUI.backgroundColor = oldColor;
    }

    private void DrawDebugSection(VolumeProcessor model)
    {
        _showDebug = EditorGUILayout.Foldout(_showDebug, "Debug", true);
        if (!_showDebug) return;

        EditorGUILayout.LabelField("Build Version: " + model.BuildVersion);
        EditorGUILayout.LabelField("Initialized: " + model.Initialized);

        model.rebuildOnMoveRelease = EditorGUILayout.Toggle("Rebuild On Move Release", model.rebuildOnMoveRelease);
        if (model.rebuildOnMoveRelease)
            model.moveReleaseDelaySeconds = EditorGUILayout.FloatField("Move Release Delay (s)", Mathf.Max(0f, model.moveReleaseDelaySeconds));
    }

    private void DrawRebuildButton(VolumeProcessor model)
    {
        if (GUILayout.Button("Rebuild Model", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();
            model.RebuildModel();
            EditorUtility.SetDirty(model);
        }
    }

    private SerializedProperty SafeProp(string name) => serializedObject.FindProperty(name);
}
