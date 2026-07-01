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
    private bool _showPreview = false;
    private bool _showObjects = true;
    private bool _showBenchmark = true;
    private bool _suppressAutoRebuildThisFrame;

    /// <summary>Draws the custom inspector for model pipeline and rebuild controls.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VolumeModel model = (VolumeModel)target;
        _suppressAutoRebuildThisFrame = false;
        EditorGUI.BeginChangeCheck();

        DrawBuilder(model);

        GUILayout.Space(10);

        DrawPreviewSettings(model);

        GUILayout.Space(10);

        DrawRendering(model);

        GUILayout.Space(10);

        DrawMeshingSettings(model);

        GUILayout.Space(10);

        DrawRebuildSettings(model);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            if (model.ShouldAutoRebuildOnChange() && !_suppressAutoRebuildThisFrame)
                model.RebuildModel();

            EditorUtility.SetDirty(model);

            serializedObject.Update();
        }

        GUILayout.Space(10);

        _showObjects = EditorGUILayout.Foldout(_showObjects, "Objects", true);

        if (_showObjects)
            DrawObjectCreation(model);

        GUILayout.Space(10);

        DrawBenchmarkSection(model);

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
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("storageMode"),
            new GUIContent("Storage Mode")
        );

        SerializedProperty octreeMesherProp = serializedObject.FindProperty("octreeMesherType");
        EditorGUILayout.PropertyField(
            octreeMesherProp,
            new GUIContent("Mesher")
        );

        DrawActiveSamplerSettings(model);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            Undo.RecordObject(model, "Change Volume Builder");

            if (model.ShouldAutoRebuildOnChange())
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

        if (enableChunkingProp != null)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("forceFullChunkRedraw"),
                new GUIContent("Always Redraw All Chunks")
            );
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("maxChunksPerRebuild"),
                new GUIContent("Max Chunks Per Rebuild")
            );
            if (EditorGUI.EndChangeCheck())
                _suppressAutoRebuildThisFrame = true;
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("dirtyHaloMultiplier"),
                new GUIContent("Dirty Halo Multiplier")
            );
        }

        if (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("octreeExpandDirtyNeighbors"),
                new GUIContent("Expand Dirty To Neighbor Chunks")
            );
            if (serializedObject.FindProperty("octreeExpandDirtyNeighbors").boolValue)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("octreeDirtyNeighborRings"),
                    new GUIContent("Dirty Neighbor Rings")
                );
            }
        }

        if (enableChunkingProp != null && enableChunkingProp.boolValue && chunkingProp != null)
        {
            bool uniformChunkResolution =
                serializedObject.FindProperty("uniformChunkResolution")?.boolValue ?? false;

            switch (model.dataStructure)
            {
                case VolumeDataStructure.VoxelGrid:
                    DrawChunkCountField(
                        "Chunk Count",
                        chunkingProp.FindPropertyRelative("voxelChunkCount"),
                        uniformChunkResolution
                    );
                    break;

                case VolumeDataStructure.Octree:
                case VolumeDataStructure.SparseVoxelOctree:
                    DrawChunkCountField(
                        "Chunk Count",
                        chunkingProp.FindPropertyRelative("octreeChunkCount"),
                        uniformChunkResolution
                    );
                    break;
            }

            SerializedProperty uniformChunkResolutionProp =
                serializedObject.FindProperty("uniformChunkResolution");
            if (uniformChunkResolutionProp != null)
            {
                EditorGUILayout.PropertyField(
                    uniformChunkResolutionProp,
                    new GUIContent("Uniform Chunk Resolution")
                );
            }
            else
            {
                model.uniformChunkResolution = EditorGUILayout.Toggle(
                    new GUIContent("Uniform Chunk Resolution"),
                    model.uniformChunkResolution
                );
            }
        }
    }

    private static void DrawChunkCountField(string label, SerializedProperty chunkCountProp, bool uniform)
    {
        if (chunkCountProp == null)
            return;

        Vector3Int current = chunkCountProp.vector3IntValue;
        current.x = Mathf.Max(1, current.x);
        current.y = Mathf.Max(1, current.y);
        current.z = Mathf.Max(1, current.z);

        if (!uniform)
        {
            EditorGUILayout.PropertyField(chunkCountProp, new GUIContent(label));
            return;
        }

        EditorGUI.BeginChangeCheck();
        Vector3Int edited = EditorGUILayout.Vector3IntField(label, current);
        if (!EditorGUI.EndChangeCheck())
            return;

        int uniformValue = current.x;
        if (edited.x != current.x) uniformValue = edited.x;
        else if (edited.y != current.y) uniformValue = edited.y;
        else if (edited.z != current.z) uniformValue = edited.z;

        uniformValue = Mathf.Max(1, uniformValue);
        chunkCountProp.vector3IntValue = new Vector3Int(uniformValue, uniformValue, uniformValue);
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
            case VolumeDataStructure.SparseVoxelOctree:
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
            builderProp.FindPropertyRelative("uniformResolution")
        );

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("gridExtent")
        );

        EditorGUILayout.PropertyField(
            builderProp.FindPropertyRelative("gridSize")
        );
    }

    /// <summary>Draws octree sampler and builder settings.</summary>
    private void DrawOctreeSettings()
    {
        bool isSparse = ((VolumeModel)target).dataStructure == VolumeDataStructure.SparseVoxelOctree;
        EditorGUILayout.LabelField(isSparse ? "Sparse Voxel Octree" : "Octree", EditorStyles.boldLabel);

        SerializedProperty samplerProp =
            serializedObject.FindProperty(isSparse ? "sparseVoxelOctreeSampler" : "octreeSampler");

        if (samplerProp == null)
            return;

        SerializedProperty centerProp = samplerProp.FindPropertyRelative("center");
        SerializedProperty extentProp = samplerProp.FindPropertyRelative("extent");
        SerializedProperty builderProp = samplerProp.FindPropertyRelative("builder");

        if (isSparse)
            builderProp = builderProp?.FindPropertyRelative("backend");

        if (centerProp != null)
            EditorGUILayout.PropertyField(centerProp);

        if (extentProp != null)
            EditorGUILayout.PropertyField(extentProp);

        if (builderProp != null)
        {
            SerializedProperty maxDepthProp = builderProp.FindPropertyRelative("maxDepth");
            SerializedProperty minDepthProp = builderProp.FindPropertyRelative("minDepth");

            if (maxDepthProp != null)
                EditorGUILayout.PropertyField(maxDepthProp);
            if (minDepthProp != null)
                EditorGUILayout.PropertyField(minDepthProp);

            EditorGUILayout.PropertyField(builderProp, true);
        }
    }

    /// <summary>Draws meshing settings.</summary>
    private void DrawMeshingSettings(VolumeModel model)
    {
        bool prevShowMeshing = _showMeshing;
        _showMeshing = EditorGUILayout.Foldout(
            _showMeshing,
            "Meshing",
            true
        );
        if (prevShowMeshing != _showMeshing)
            _suppressAutoRebuildThisFrame = true;

        if (!_showMeshing)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("isoLevel")
        );
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("edgeRefinementSteps"),
            new GUIContent("Edge Refinement Steps")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("useQefVertices"),
            new GUIContent("Use QEF Vertices")
        );
        if (serializedObject.FindProperty("useQefVertices").boolValue)
        {
            SerializedProperty modeProp = serializedObject.FindProperty("qefVertexMode");
            EditorGUILayout.PropertyField(
                modeProp,
                new GUIContent("QEF Vertex Mode")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefBlendFactor"),
                new GUIContent("QEF Blend Factor")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefSnapEpsilon"),
                new GUIContent("QEF Snap Epsilon")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefMaxOffsetCells"),
                new GUIContent("QEF Max Offset Cells")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefEnableMultiHermite"),
                new GUIContent("Enable Multi-Hermite")
            );
            if (serializedObject.FindProperty("qefEnableMultiHermite").boolValue)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefHermiteSamplesPerEdge"),
                    new GUIContent("Hermite Samples Per Edge")
                );
            }
            if (modeProp != null && (QefVertexMode)modeProp.enumValueIndex == QefVertexMode.QefAxisSnap)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefAxisSnapStrength"),
                    new GUIContent("Axis Snap Strength")
                );
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefRobustKernel"),
                new GUIContent("QEF Robust Kernel")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefRobustScale"),
                new GUIContent("QEF Robust Scale")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefIrlsIterations"),
                new GUIContent("QEF IRLS Iterations")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefUseAnisotropicRegularization"),
                new GUIContent("QEF Anisotropic Regularization")
            );
            if (serializedObject.FindProperty("qefUseAnisotropicRegularization").boolValue)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefAnisotropicStrength"),
                    new GUIContent("QEF Anisotropic Strength")
                );
            }
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("qefFeatureWeightMode"),
                new GUIContent("QEF Feature Weights")
            );
            if ((QefFeatureClassWeightMode)serializedObject.FindProperty("qefFeatureWeightMode").enumValueIndex
                != QefFeatureClassWeightMode.Off)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefSurfaceWeight"),
                    new GUIContent("Surface Weight")
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefEdgeWeight"),
                    new GUIContent("Edge Weight")
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("qefCornerWeight"),
                    new GUIContent("Corner Weight")
                );
            }
        }

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("recalculateNormals")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("recalculateBounds")
        );

        EditorGUILayout.Space(8);

        bool prevShowDebug = _showDebug;
        _showDebug = EditorGUILayout.Foldout(
            _showDebug,
            "Debug",
            true
        );
        if (prevShowDebug != _showDebug)
            _suppressAutoRebuildThisFrame = true;

        if (!_showDebug)
            return;

        SerializedProperty drawChildGizmosProp =
            serializedObject.FindProperty("drawChildGizmos");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(drawChildGizmosProp);
        if (EditorGUI.EndChangeCheck())
            _suppressAutoRebuildThisFrame = true;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("drawChunkGizmosAlways")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("logChunkRebuildStats")
        );
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("logRebuildDuration")
        );
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("writeBenchmarkLogsToFile")
        );
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("benchmarkLogArchiveLimit")
        );
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("profileFlatRecursiveParts"),
            new GUIContent("Profile Flat Recursive Parts")
        );

        if (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree)
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
            serializedObject.FindProperty("rebuildMode"),
            new GUIContent("Mode")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("rebuildOnMoveRelease")
        );

        if (serializedObject.FindProperty("rebuildOnMoveRelease").boolValue)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("moveReleaseDelaySeconds")
            );
        }

    }

    private void DrawPreviewSettings(VolumeModel model)
    {
        _showPreview = EditorGUILayout.Foldout(
            _showPreview,
            "Preview",
            true
        );

        if (!_showPreview)
            return;

        if (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("useFlatDualContouringPreview"),
                new GUIContent("Use Flat DC Preview")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("simplifyQefDuringPreview"),
                new GUIContent("Simplify QEF During Preview")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("previewEdgeRefinementSteps"),
                new GUIContent("Preview Edge Refinement Steps")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("usePreviewDepthWhileInteracting"),
                new GUIContent("Use Preview Depth While Interacting")
            );

            if (serializedObject.FindProperty("usePreviewDepthWhileInteracting").boolValue)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("previewInteractionMaxDepth"),
                    new GUIContent("Preview Max Depth")
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("previewInteractionHoldSeconds"),
                    new GUIContent("Preview Hold Seconds")
                );
                if (EditorGUI.EndChangeCheck())
                    _suppressAutoRebuildThisFrame = true;
            }
            return;
        }

        if (model.dataStructure == VolumeDataStructure.VoxelGrid)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("usePreviewResolutionWhileInteracting"),
                new GUIContent("Use Preview Resolution While Interacting")
            );

            if (serializedObject.FindProperty("usePreviewResolutionWhileInteracting").boolValue)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("previewVoxelUniformResolution"),
                    new GUIContent("Uniform Preview Resolution")
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("previewVoxelGridSize"),
                    new GUIContent("Preview Grid Size")
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("previewInteractionHoldSeconds"),
                    new GUIContent("Preview Hold Seconds")
                );
                if (EditorGUI.EndChangeCheck())
                    _suppressAutoRebuildThisFrame = true;
            }
        }
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

    private void DrawBenchmarkSection(VolumeModel model)
    {
        _showBenchmark = EditorGUILayout.Foldout(_showBenchmark, "Benchmark", true);

        if (!_showBenchmark)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("benchmarkType"),
            new GUIContent("Type")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("benchmarkRuns"),
            new GUIContent("Runs")
        );

        VolumeBenchmarkType benchmarkType = (VolumeBenchmarkType)serializedObject
            .FindProperty("benchmarkType")
            .enumValueIndex;
        bool isDirtyMoveBenchmark =
            benchmarkType == VolumeBenchmarkType.DirtyMove ||
            benchmarkType == VolumeBenchmarkType.DirtyMoveSweep;
        if (isDirtyMoveBenchmark)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Dirty Move", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("dirtyMoveBenchmarkObject"),
                new GUIContent("Move Object")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("dirtyMoveBenchmarkOffset"),
                new GUIContent("Move Offset")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("visualizeDirtyMoveBenchmark"),
                new GUIContent("Visual Steps")
            );
            if (serializedObject.FindProperty("visualizeDirtyMoveBenchmark").boolValue)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("dirtyMoveBenchmarkStepDelayMs"),
                    new GUIContent("Step Delay (ms)")
                );
            }
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("restoreDirtyMoveBenchmarkObject"),
                new GUIContent("Restore Object")
            );
        }

        if (GUILayout.Button("Run Benchmark", GUILayout.Height(25)))
        {
            serializedObject.ApplyModifiedProperties();

            if (model.benchmarkType == VolumeBenchmarkType.DirtyMove ||
                model.benchmarkType == VolumeBenchmarkType.DirtyMoveSweep)
            {
                bool visualSteps = serializedObject.FindProperty("visualizeDirtyMoveBenchmark").boolValue;
                model.RunDirtyMoveBenchmark(visualSteps);
            }
            else
            {
                model.RunRebuildBenchmark();
            }

            EditorUtility.SetDirty(model);
        }
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
