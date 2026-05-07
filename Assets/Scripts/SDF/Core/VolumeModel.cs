#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSampler))]
[RequireComponent(typeof(VolumeSceneComposer))]
[RequireComponent(typeof(VolumeMeshRenderer))]
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

        var composer = GetComponent<VolumeSceneComposer>();

        if (!composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

        RebuildModel();
    }

    public void RebuildModel()
    {
        var composer = GetComponent<VolumeSceneComposer>();
        var sampler = GetComponent<VolumeSampler>();
        var renderer = GetComponent<VolumeMeshRenderer>();

        composer.RebuildComposition();
        sampler.MarkDirty();
        renderer.RebuildMesh();
    }
}