using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SDFSampler))]
public class SDFSceneComposer : MonoBehaviour
{
    public List<SDFObject> objects = new();

    private SDFSampler _sampler;

    private void Awake()
    {
        _sampler = GetComponent<SDFSampler>();
    }

    [ContextMenu("Rebuild Composition")]
    public void RebuildComposition()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();

        objects.RemoveAll(o => o == null);

        Debug.Log($"[SDFSceneComposer] Objects: {objects.Count}");

        foreach (var obj in objects)
        {
            Debug.Log($"[SDFSceneComposer] Object: {obj.name}, Role: {obj.role}, Shape: {obj.shapeType}");
        }

        SceneCompositeSDF composite = new SceneCompositeSDF(transform, objects);
        _sampler.SetRuntimeSDF(composite);
    }
}