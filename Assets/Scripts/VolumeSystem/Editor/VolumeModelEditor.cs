using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeModel))]
public class VolumeModelEditor : Editor
{
    private bool _showBuilder = true;
    private bool _showRendering = true;
    private bool _showMeshing = false;
    private bool _showDebug = false;
    private bool _showRebuild = true;
    private bool _showObjects = false;

    /// <summary>Draws the custom inspector for model pipeline and rebuild controls.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeModel model = (VolumeModel)target;

        DrawBuilder(model);

        GUILayout.Space(10);

        EditorGUI.BeginChangeCheck();

        DrawRendering(model);

        GUILayout.Space(10);

        DrawMeshingSettings(model);

        GUILayout.Space(10);

        DrawRebuildSettings(model);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            if (model.autoRebuildOnChange)
                model.RebuildModel();

            EditorUtility.SetDirty(model);

            serializedObject.Update();
        }

        GUILayout.Space(10);

        _showObjects = EditorGUILayout.Foldout(_showObjects, "Objects", true);

        if (_showObjects)
            DrawObjectCreation(model);

        GUILayout.Space(10);

        DrawRebuildButton(model);

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Draws data structure and active builder settings.</summary>
    private void DrawBuilder(VolumeModel model)
    {
        _showBuilder = EditorGUILayout.Foldout(_showBuilder, "Builder", true);

        if (!_showBuilder)
            return;

        SerializedProperty dataStructureProp =
            serializedObject.FindProperty("dataStructure");

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(
            dataStructureProp,
            new GUIContent("Data Structure")
        );

        DrawActiveSamplerSettings(model);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Change Volume Builder");

            if (model.autoRebuildOnChange)
                model.RebuildModel();

            EditorUtility.SetDirty(model);

            serializedObject.Update();
        }
    }

    /// <summary>Draws rendering and chunking controls.</summary>
    private void DrawRendering(VolumeModel model)
    {
        _showRendering = EditorGUILayout.Foldout(_showRendering, "Rendering", true);

        if (!_showRendering)
            return;

        SerializedProperty enableChunkingProp =
            serializedObject.FindProperty("enableChunking");
        SerializedProperty chunkingProp =
            serializedObject.FindProperty("chunking");

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("surfaceMaterial"),
            new GUIContent("Surface Material")
        );

        if (enableChunkingProp != null)
            EditorGUILayout.PropertyField(enableChunkingProp, new GUIContent("Enable Chunking"));

        if (enableChunkingProp != null && enableChunkingProp.boolValue && chunkingProp != null)
        {
            switch (model.dataStructure)
            {
                case VolumeDataStructure.VoxelGrid:
                    EditorGUILayout.PropertyField(
                        chunkingProp.FindPropertyRelative("voxelChunkCount"),
                        new GUIContent("Chunk Count")
                    );
                    break;

                case VolumeDataStructure.Octree:
                    EditorGUILayout.PropertyField(
                        chunkingProp.FindPropertyRelative("octreeTargetTrianglesPerChunk"),
                        new GUIContent("Target Triangles/Chunk")
                    );
                    EditorGUILayout.PropertyField(
                        chunkingProp.FindPropertyRelative("octreeEstimatedTrianglesPerLeaf"),
                        new GUIContent("Estimated Triangles/Leaf")
                    );
                    EditorGUILayout.PropertyField(
                        chunkingProp.FindPropertyRelative("octreeMaxLeafNodesPerChunk"),
                        new GUIContent("Max Leafs/Chunk")
                    );
                    break;
            }
        }
    }

    /// <summary>Draws the sampler settings for the active data structure.</summary>
    private void DrawActiveSamplerSettings(VolumeModel model)
    {
        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                DrawVoxelGridSettings();
                break;

            case VolumeDataStructure.Octree:
                DrawOctreeSettings();
                break;
        }
    }

    /// <summary>Draws voxel grid builder settings.</summary>
    private void DrawVoxelGridSettings()
    {
        EditorGUILayout.LabelField("Voxel Grid", EditorStyles.boldLabel);

        SerializedProperty samplerProp =
            serializedObject.FindProperty("voxelGridSampler");

        if (samplerProp == null)
            return;

        SerializedProperty builderProp =
            samplerProp.FindPropertyRelative("builder");

        if (builderProp == null)
            return;

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("uniformExtent")
        );

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("gridExtent")
        );

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("uniformResolution")
        );

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("gridSize")
        );
    }

    /// <summary>Draws octree sampler and builder settings.</summary>
    private void DrawOctreeSettings()
    {
        EditorGUILayout.LabelField("Octree", EditorStyles.boldLabel);

        SerializedProperty samplerProp =
            serializedObject.FindProperty("octreeSampler");

        if (samplerProp == null)
            return;

        SerializedProperty centerProp =
            samplerProp.FindPropertyRelative("center");

        SerializedProperty extentProp =
            samplerProp.FindPropertyRelative("extent");

        SerializedProperty builderProp =
            samplerProp.FindPropertyRelative("builder");

        if (centerProp != null)
            EditorGUILayout.PropertyField(centerProp);

        if (extentProp != null)
            EditorGUILayout.PropertyField(extentProp);

        if (builderProp != null)
            EditorGUILayout.PropertyField(builderProp, true);
    }

    /// <summary>Draws meshing settings.</summary>
    private void DrawMeshingSettings(VolumeModel model)
    {
        _showMeshing = EditorGUILayout.Foldout(
            _showMeshing,
            "Meshing",
            true
        );

        if (!_showMeshing)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("isoLevel")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("recalculateNormals")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("recalculateBounds")
        );

        EditorGUILayout.Space(8);

        _showDebug = EditorGUILayout.Foldout(
            _showDebug,
            "Debug",
            true
        );

        if (!_showDebug)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("drawChildGizmos")
        );

        if (model.dataStructure == VolumeDataStructure.Octree)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("renderOctreeDebugCubes")
            );
        }
    }

    /// <summary>Draws automatic and realtime rebuild settings.</summary>
    private void DrawRebuildSettings(VolumeModel model)
    {
        _showRebuild = EditorGUILayout.Foldout(
            _showRebuild,
            "Rebuild",
            true
        );

        if (!_showRebuild)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("autoRebuildOnChange")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("rebuildEveryFrame")
        );
    }

    /// <summary>Draws controls for adding, removing, and clearing volume objects.</summary>
    private void DrawObjectCreation(VolumeModel model)
    {
        EditorGUILayout.LabelField("Create SDF Object", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("shapeToAdd")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("roleToAdd")
        );

        GUILayout.Space(5);

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

        GUILayout.Space(5);

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

    /// <summary>Draws the manual rebuild button.</summary>
    private void DrawRebuildButton(VolumeModel model)
    {
        if (GUILayout.Button("Rebuild Model", GUILayout.Height(30)))
        {
            serializedObject.ApplyModifiedProperties();

            model.RebuildModel();

            EditorUtility.SetDirty(model);
        }
    }
}
