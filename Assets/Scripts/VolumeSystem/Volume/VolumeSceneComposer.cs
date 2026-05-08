using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(VolumeSampler))]
public class VolumeSceneComposer : MonoBehaviour
{
    public List<VolumeObject> objects = new();

    private VolumeSampler _sampler;

    private void Awake()
    {
        _sampler = GetComponent<VolumeSampler>();
    }

    [ContextMenu("Rebuild Composition")]
    public void RebuildComposition()
    {
        if (_sampler == null)
            _sampler = GetComponent<VolumeSampler>();

        objects.RemoveAll(o => o == null);

        RenameChildren();

        SceneCompositeSDF composite = new SceneCompositeSDF(transform, objects);
        _sampler.SetRuntimeSource(composite);
    }

    public void RenameChildren()
    {
        objects.RemoveAll(o => o == null);

        for (int i = 0; i < objects.Count; i++)
        {
            VolumeObject obj = objects[i];

            string roleName = obj.role.ToString();
            string shapeName = obj.shapeType.ToString();

            obj.name = $"VolumeObject_{i:00}_{shapeName}_{roleName}";
        }
    }

    public void MarkDirtyAndRebuild()
    {
        if (_sampler == null)
            _sampler = GetComponent<VolumeSampler>();

        RebuildComposition();

        if (_sampler != null)
            _sampler.MarkDirty();
    }
}