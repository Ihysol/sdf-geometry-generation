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

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
[RequireComponent(typeof(VolumeMeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeModel : MonoBehaviour
{


#if UNITY_EDITOR
    private bool _editorRebuildQueued;

    private void Reset()
    {
        MoveToTop();
    }

    private void OnValidate()
    {
        MoveToTop();

        voxelGridSampler?.builder?.Validate();

        // if (!autoRebuildOnChange)
        //     return;

        // if (_editorRebuildQueued)
        //     return;

        // _editorRebuildQueued = true;
        // EditorApplication.delayCall += DelayedEditorRebuild;
    }



    private void DelayedEditorRebuild()
    {
        if (this == null)
            return;

        _editorRebuildQueued = false;

        RebuildModel();
    }

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

    private void Update()
    {
        if (rebuildEveryFrame)
            RebuildModel();
    }

    public void AddSelectedObject()
    {
        AddObject(shapeToAdd, roleToAdd);
    }

    public void AddObject(VolumeShapeType shape, VolumeOperationRole role)
    {
        GameObject child = new GameObject($"VolumeObject_{shape}_{role}");
        child.transform.SetParent(transform, false);

        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = shape;
        volumeObject.role = role;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (!composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

        RebuildModel();
    }

    public void RebuildModel()
    {
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

        VolumeMeshRenderer renderer = GetComponent<VolumeMeshRenderer>();

        if (renderer != null)
            renderer.RebuildMesh(this);
    }

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

    public void ClearObjects()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        for (int i = composer.objects.Count - 1; i >= 0; i--)
        {
            VolumeObject obj = composer.objects[i];

            if (obj == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(obj.gameObject);
            else
                Destroy(obj.gameObject);
#else
            Destroy(obj.gameObject);
#endif
        }

        composer.objects.Clear();
        RebuildModel();
    }

    public void RemoveLastObject()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.objects.RemoveAll(o => o == null);

        if (composer.objects.Count == 0)
            return;

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

        RebuildModel();
    }

    private void OnDrawGizmos()
    {
        DrawActiveBoundsGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawActiveBoundsGizmo(true);
    }

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
}