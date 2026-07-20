using System.Collections.Generic;
using UnityEngine;

public class VolumeSceneComposer : MonoBehaviour, IScalarFieldSource
{
    public List<VolumeObject> objects = new();

    private SceneCompositeSDF _composite;
    private SdfSceneSnapshot _snapshot;

    public SdfSceneSnapshot Snapshot => _snapshot;

    /// <summary>Returns the built-in snapshot when all shapes are supported by Burst evaluation.</summary>
    public bool TryGetBuiltInSnapshot(out SdfSceneSnapshot snapshot)
    {
        if (_snapshot == null)
            RebuildComposition();
        snapshot = _snapshot;
        return snapshot != null && !snapshot.HasUnsupportedShapes;
    }

    /// <summary>Refreshes the composite SDF from the current object list.</summary>
    [ContextMenu("Rebuild Composition")]
    public void RebuildComposition()
    {
        // Purge stale MonoBehaviour references (invalid after domain reload / script recompilation)
        objects.RemoveAll(o => o == null);

        // Recover live VolumeObjects from scene hierarchy — serialized list refs
        // become invalid after Unity recompiles scripts, but the GameObjects persist.
        Transform root = ObjectsRoot;
        if (root != null)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                VolumeObject vo = root.GetChild(i).GetComponent<VolumeObject>();
                if (vo != null && !objects.Contains(vo))
                    objects.Add(vo);
            }
        }

        // Double-check after recovery
        objects.RemoveAll(o => o == null);

        RenameChildren();

        _composite = new SceneCompositeSDF(transform, objects);
        _snapshot = new SdfSceneSnapshot(transform, objects);

        Debug.Log($"[Composer] Rebuilt: {objects.Count} objects");
    }

    private Transform ObjectsRoot
    {
        get
        {
            Transform existing = transform.Find("Objects");
            if (existing != null) return existing;
            GameObject go = new GameObject("Objects");
            go.transform.SetParent(transform, false);
            return go.transform;
        }
    }

    /// <summary>Samples the composed SDF at a world-space position (per IScalarFieldSource contract).</summary>
    public float Evaluate(Vector3 worldPoint)
    {
        if (_composite == null)
            RebuildComposition();

        if (_composite == null)
            return 1f;

        return _composite.Evaluate(worldPoint);
    }

    /// <summary>Renames registered child objects to match their order and role.</summary>
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

    /// <summary>Refreshes the composition and asks the owning model to rebuild.</summary>
    public void MarkDirtyAndRebuild()
    {
        RebuildComposition();

        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
            model.RebuildModel();
    }

    public void MarkDirtyAndRebuild(Bounds dirtyBounds)
    {
        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
            model.MarkDirtyBounds(dirtyBounds);

        RebuildComposition();

        if (model != null)
            model.RebuildModel();
    }

    public void MarkDirtyAndRebuild(Bounds dirtyBounds, IReadOnlyList<Bounds> dirtyBoundsParts)
    {
        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
        {
            if (dirtyBoundsParts == null || dirtyBoundsParts.Count == 0)
            {
                model.MarkDirtyBounds(dirtyBounds);
            }
            else
            {
                for (int i = 0; i < dirtyBoundsParts.Count; i++)
                    model.MarkDirtyBounds(dirtyBoundsParts[i]);
            }
        }

        RebuildComposition();

        if (model != null)
            model.RebuildModel();
    }

}
