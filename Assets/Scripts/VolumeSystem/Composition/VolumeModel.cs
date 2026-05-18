#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using UnityEngine;

public enum VolumeDataStructure
{
    VoxelGrid,
    Octree
}

public enum QefVertexMode
{
    AverageCrossings,
    QefFast,
    QefFeaturePreserving,
    QefAxisSnap
}

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
public class VolumeModel : MonoBehaviour
{
    private readonly System.Collections.Generic.List<Bounds> _chunkBoundsCache = new();
    private bool _hasDirtyBounds;
    private Bounds _dirtyBounds;

    [Header("Rendering")]
    public bool enableChunking = true;
    public bool forceFullChunkRedraw = false;
    public int maxChunksPerRebuild = 8;
    public bool octreeExpandDirtyNeighbors = true;
    public int octreeDirtyNeighborRings = 2;
    public float dirtyHaloMultiplier = 3f;
    public Material surfaceMaterial;
    public ChunkingSettings chunking = new ChunkingSettings
    {
        voxelChunkCount = new Vector3Int(2, 2, 2),
        octreeChunkCount = new Vector3Int(2, 2, 2),
        octreeTargetTrianglesPerChunk = 10000,
        octreeEstimatedTrianglesPerLeaf = 12,
        octreeMaxLeafNodesPerChunk = 1024
    };

    private Transform ObjectsRoot
    {
        get
        {
            Transform existing = transform.Find("Objects");

            if (existing != null)
                return existing;

            GameObject go = new GameObject("Objects");
            go.transform.SetParent(transform, false);

            return go.transform;
        }
    }


#if UNITY_EDITOR
    /// <summary>Keeps VolumeModel first in the component stack when added.</summary>
    private void Reset()
    {
        MoveToTop();
    }

    /// <summary>Validates nested sampler settings after inspector edits.</summary>
    private void OnValidate()
    {
        MoveToTop();

        voxelGridSampler?.builder?.Validate();
        maxChunksPerRebuild = Mathf.Max(1, maxChunksPerRebuild);
        octreeDirtyNeighborRings = Mathf.Max(0, octreeDirtyNeighborRings);
        dirtyHaloMultiplier = Mathf.Max(0f, dirtyHaloMultiplier);
        moveReleaseDelaySeconds = Mathf.Max(0f, moveReleaseDelaySeconds);
        qefBlendFactor = Mathf.Clamp01(qefBlendFactor);
        qefSnapEpsilon = Mathf.Max(0f, qefSnapEpsilon);
        qefMaxOffsetCells = Mathf.Max(0f, qefMaxOffsetCells);
        qefAxisSnapStrength = Mathf.Max(1f, qefAxisSnapStrength);
    }

    /// <summary>Moves this component above companion components in the inspector.</summary>
    private void MoveToTop()
    {
        while (ComponentUtility.MoveComponentUp(this)) { }
    }
#endif

    [Header("Pipeline")]
    public VolumeDataStructure dataStructure = VolumeDataStructure.Octree;

    [Header("Samplers")]
    public VoxelGridSampler voxelGridSampler = new();
    public OctreeVolumeSampler octreeSampler = new();

    [Header("Meshing")]
    public float isoLevel = 0f;
    public bool useQefVertices = true;
    public QefVertexMode qefVertexMode = QefVertexMode.QefAxisSnap;
    [Range(0f, 1f)]
    public float qefBlendFactor = 0.5f;
    [Min(0f)]
    public float qefSnapEpsilon = 0.015f;
    [Min(0f)]
    public float qefMaxOffsetCells = 0.75f;
    [Min(1f)]
    public float qefAxisSnapStrength = 2.5f;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;

    [Header("Rebuild")]
    public bool autoRebuildOnChange = true;
    public bool rebuildEveryFrame = false;
    public bool rebuildOnMoveRelease = true;
    public float moveReleaseDelaySeconds = 0.5f;

    [Header("Debug")]
    public bool drawChildGizmos = true;
    public bool drawChunkGizmosAlways = false;
    public bool renderOctreeDebugCubes = false;
    public bool logChunkRebuildStats = false;

    [Header("Add Object")]
    public VolumeShapeType shapeToAdd = VolumeShapeType.Sphere;
    public VolumeOperationRole roleToAdd = VolumeOperationRole.Add;

    /// <summary>Continuously rebuilds the model when realtime rebuild is enabled.</summary>
    private void Update()
    {
        if (rebuildEveryFrame)
            RebuildModel();
    }

    /// <summary>Adds an object using the currently selected inspector defaults.</summary>
    public void AddSelectedObject()
    {
        AddObject(shapeToAdd, roleToAdd);
    }

    /// <summary>Creates a new child volume object and rebuilds the model.</summary>
    public void AddObject(VolumeShapeType shape, VolumeOperationRole role)
    {
        GameObject child = new GameObject($"VolumeObject_{shape}_{role}");
        child.transform.SetParent(ObjectsRoot, false);

        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = shape;
        volumeObject.role = role;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (!composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

        RebuildModel();
    }

    /// <summary>Rebuilds composition, volume data, and render output.</summary>
    public void RebuildModel()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.RebuildComposition();

        IScalarFieldSource source = composer;
        bool didIncrementalVoxelUpdate = false;
        bool hasDirtyBounds = TryGetPendingDirtyBounds(out Bounds dirtyBounds);

        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                if (hasDirtyBounds)
                {
                    didIncrementalVoxelUpdate = voxelGridSampler.RebuildVolumeRegion(source, dirtyBounds, 3);
                }

                if (!didIncrementalVoxelUpdate)
                {
                    voxelGridSampler.MarkDirty();
                    voxelGridSampler.RebuildVolume(source);
                    ClearDirtyBounds();
                }
                break;

            case VolumeDataStructure.Octree:
                octreeSampler.builder.useQefVertices = useQefVertices;
                octreeSampler.builder.qefVertexMode = qefVertexMode;
                octreeSampler.builder.qefBlendFactor = qefBlendFactor;
                octreeSampler.builder.qefSnapEpsilon = qefSnapEpsilon;
                octreeSampler.builder.qefMaxOffsetCells = qefMaxOffsetCells;
                octreeSampler.builder.qefAxisSnapStrength = qefAxisSnapStrength;
                if (hasDirtyBounds)
                {
                    bool didIncrementalOctreeUpdate =
                        octreeSampler.RebuildVolumeRegion(source, dirtyBounds);

                    if (didIncrementalOctreeUpdate)
                        break;

#if UNITY_EDITOR
                    if (logChunkRebuildStats)
                        Debug.LogWarning("Octree incremental rebuild failed; falling back to full rebuild.");
#endif
                }

                octreeSampler.MarkDirty();
                octreeSampler.RebuildVolume(source);
                ClearDirtyBounds();
                break;
        }

        if (!enableChunking)
            ClearDirtyBounds();

        RenderOutput.Rebuild(this);
    }

    /// <summary>Returns the currently active sampled volume data.</summary>
    public IVolumeData GetActiveVolume()
    {
        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                return voxelGridSampler.Volume;

            case VolumeDataStructure.Octree:
                return octreeSampler.Volume;

            default:
                return null;
        }
    }

    /// <summary>Deletes all child volume objects and clears generated output.</summary>
    public void ClearObjects()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        VolumeObject[] allObjects = GetComponentsInChildren<VolumeObject>(true);

        for (int i = allObjects.Length - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(allObjects[i].gameObject);
            else
                Destroy(allObjects[i].gameObject);
#else
        Destroy(allObjects[i].gameObject);
#endif
        }

        composer.objects.Clear();
        composer.RebuildComposition();

        ClearRenderOutput();
    }

    /// <summary>Deletes the last registered volume object and rebuilds if needed.</summary>
    public void RemoveLastObject()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.objects.RemoveAll(o => o == null);

        if (composer.objects.Count == 0)
        {
            ClearRenderOutput();
            return;
        }

        VolumeObject last = composer.objects[composer.objects.Count - 1];
        composer.objects.RemoveAt(composer.objects.Count - 1);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(last.gameObject);
        else
            Destroy(last.gameObject);
#else
    Destroy(last.gameObject);
#endif

        composer.RebuildComposition();

        if (composer.objects.Count == 0)
        {
            ClearRenderOutput();
            return;
        }

        RebuildModel();
    }

    /// <summary>Draws the active volume bounds in the scene view.</summary>
    private void OnDrawGizmos()
    {
        DrawActiveBoundsGizmo(false);
    }

    /// <summary>Draws selected-state bounds and optional octree debug nodes.</summary>
    private void OnDrawGizmosSelected()
    {
        DrawActiveBoundsGizmo(true);
    }

    /// <summary>Draws the active sampler bounds and optional octree leaf boxes.</summary>
    private void DrawActiveBoundsGizmo(bool selected)
    {
        Bounds bounds;

        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                bounds = voxelGridSampler.builder.Bounds;
                break;

            case VolumeDataStructure.Octree:
                bounds = octreeSampler.builder.Bounds;
                break;

            default:
                return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = selected
            ? new Color(0f, 1f, 1f, 1f)
            : new Color(0f, 1f, 1f, 0.35f);

        Gizmos.DrawWireCube(bounds.center, bounds.size);

        if (dataStructure == VolumeDataStructure.Octree &&
            renderOctreeDebugCubes &&
            octreeSampler.Volume != null)
        {
            DrawOctreeNode(octreeSampler.Volume.Root);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>Recursively draws octree leaves that contain surface samples.</summary>
    private void DrawOctreeNode(OctreeNode node)
    {
        if (node == null)
            return;

        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.2f);

                Gizmos.DrawWireCube(
                    node.Bounds.center,
                    node.Bounds.size
                );
            }

            return;
        }

        if (node.Children == null)
            return;

        for (int i = 0; i < node.Children.Length; i++)
        {
            DrawOctreeNode(node.Children[i]);
        }
    }

    private VolumeRenderOutput RenderOutput
    {
        get
        {
            Transform existing = transform.Find("VolumeRenderOutput");

            if (existing != null)
            {
                VolumeRenderOutput output = existing.GetComponent<VolumeRenderOutput>();

                if (output != null)
                    return output;
            }

            GameObject go = new GameObject("VolumeRenderOutput");
            go.transform.SetParent(transform, false);

            return go.AddComponent<VolumeRenderOutput>();
        }
    }

    /// <summary>Clears the current render output if it exists.</summary>
    private void ClearRenderOutput()
    {
        VolumeRenderOutput output = RenderOutput;

        if (output != null)
            output.Clear();
    }

    public bool TryGetChunkBounds(out System.Collections.Generic.List<Bounds> bounds)
    {
        bounds = _chunkBoundsCache;
        bounds.Clear();

        IVolumeData activeVolume = GetActiveVolume();

        if (activeVolume is not IChunkLayoutVolume chunkLayoutVolume)
        {
            bounds = null;
            return false;
        }

        chunkLayoutVolume.BuildChunkBounds(chunking, bounds);

        if (bounds.Count == 0)
            bounds.Add(activeVolume.Bounds);

        return true;
    }

    public void MarkDirtyBounds(Bounds dirtyBounds)
    {
        if (!_hasDirtyBounds)
        {
            _dirtyBounds = dirtyBounds;
            _hasDirtyBounds = true;
            return;
        }

        _dirtyBounds.Encapsulate(dirtyBounds);
    }

    public bool TryConsumeDirtyBounds(out Bounds dirtyBounds)
    {
        if (_hasDirtyBounds)
        {
            dirtyBounds = _dirtyBounds;
            _hasDirtyBounds = false;
            return true;
        }

        dirtyBounds = default;
        return false;
    }

    public bool TryGetPendingDirtyBounds(out Bounds dirtyBounds)
    {
        if (_hasDirtyBounds)
        {
            dirtyBounds = _dirtyBounds;
            return true;
        }

        dirtyBounds = default;
        return false;
    }

    private void ClearDirtyBounds()
    {
        _hasDirtyBounds = false;
    }

}
