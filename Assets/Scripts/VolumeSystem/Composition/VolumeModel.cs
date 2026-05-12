#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using UnityEngine;

public enum VolumeRenderMode
{
    SingleMesh,
    Chunked
}

public enum VolumeDataStructure
{
    VoxelGrid,
    Octree
}

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
public class VolumeModel : MonoBehaviour
{

    [Header("Rendering")]
    public VolumeRenderMode renderMode = VolumeRenderMode.Chunked;

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
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;

    [Header("Rebuild")]
    public bool autoRebuildOnChange = true;
    public bool rebuildEveryFrame = false;

    [Header("Debug")]
    public bool drawChildGizmos = true;
    public bool renderOctreeDebugCubes = false;

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
        RenderOutput.Clear();

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.RebuildComposition();

        IScalarFieldSource source = composer;

        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                voxelGridSampler.MarkDirty();
                voxelGridSampler.RebuildVolume(source);
                break;

            case VolumeDataStructure.Octree:
                octreeSampler.MarkDirty();
                octreeSampler.RebuildVolume(source);
                break;
        }

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
}
