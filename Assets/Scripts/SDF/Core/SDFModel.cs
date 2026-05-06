#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(SDFSceneComposer))]
[RequireComponent(typeof(SDFDualContouringRenderer))]
public class SDFModel : MonoBehaviour
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
    public SDFShapeType shapeToAdd = SDFShapeType.Sphere;
    public SDFOperationRole roleToAdd = SDFOperationRole.Add;

    public void AddSelectedObject()
    {
        AddObject(shapeToAdd, roleToAdd);
    }

    public void AddObject(SDFShapeType shape, SDFOperationRole role)
    {
        GameObject child = new GameObject($"SDFObject_{shape}_{role}");

        child.transform.SetParent(transform, false);

        SDFObject sdfObject = child.AddComponent<SDFObject>();
        sdfObject.shapeType = shape;
        sdfObject.role = role;

        var composer = GetComponent<SDFSceneComposer>();

        if (!composer.objects.Contains(sdfObject))
            composer.objects.Add(sdfObject);

        RebuildModel();
    }

    public void RebuildModel()
    {
        var composer = GetComponent<SDFSceneComposer>();
        var sampler = GetComponent<SDFSampler>();
        var renderer = GetComponent<SDFDualContouringRenderer>();

        composer.RebuildComposition();
        sampler.MarkDirty();
        renderer.RebuildMesh();
    }
}