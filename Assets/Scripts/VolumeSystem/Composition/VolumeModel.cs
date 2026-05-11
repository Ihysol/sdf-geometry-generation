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
    private void Reset()
    {
        MoveToTop();
    }

    private void OnValidate()
    {
        MoveToTop();
    }

    private void MoveToTop()
    {
        while (ComponentUtility.MoveComponentUp(this)) { }
    }
#endif

    [Header("Pipeline")]
    public VolumeDataStructure dataStructure = VolumeDataStructure.VoxelGrid;

    [Header("Samplers")]
    public VoxelGridSampler voxelGridSampler = new();
    public OctreeVolumeSampler octreeSampler = new();

    [Header("Meshing")]
    public float isoLevel = 0f;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;

    [Header("Debug")]
    public bool renderOctreeDebugCubes = true;

    [Header("Add Object")]
    public VolumeShapeType shapeToAdd = VolumeShapeType.Sphere;
    public VolumeOperationRole roleToAdd = VolumeOperationRole.Add;

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
}