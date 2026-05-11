using System.Collections.Generic;
using UnityEngine;

public class VolumeSceneComposer : MonoBehaviour, IScalarFieldSource
{
    public List<VolumeObject> objects = new();

    private SceneCompositeSDF _composite;

    [ContextMenu("Rebuild Composition")]
    public void RebuildComposition()
    {
        objects.RemoveAll(o => o == null);

        RenameChildren();

        _composite = new SceneCompositeSDF(transform, objects);
    }

    public float Evaluate(Vector3 p)
    {
        if (_composite == null)
            RebuildComposition();

        if (_composite == null)
            return 1f;

        return _composite.Evaluate(p);
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
        RebuildComposition();

        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
            model.RebuildModel();
    }
}