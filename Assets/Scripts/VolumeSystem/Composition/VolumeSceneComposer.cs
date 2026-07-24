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
        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
            model.RebuildModel(); // RebuildModel() already calls RebuildComposition() internally
    }

    public void MarkDirtyAndRebuild(Bounds dirtyBounds)
    {
        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
        {
            // Dirty bounds from VolumeObject are in Objects-parent-local space.
            // Transform to world-space so WorldBoundsToIntBounds targets correct cells.
            Bounds world = TransformBoundsToWorld(dirtyBounds);
            model.MarkDirtyBounds(world);
            model.RebuildDirty(); // RebuildDirty() already calls RebuildComposition() internally
        }
    }

    public void MarkDirtyAndRebuild(Bounds dirtyBounds, IReadOnlyList<Bounds> dirtyBoundsParts)
    {
        VolumeModel model = GetComponent<VolumeModel>();

        if (model != null)
        {
            // Encapsulate all parts in world space for accurate dirty region.
            Bounds union = TransformBoundsToWorld(dirtyBounds);
            if (dirtyBoundsParts != null)
            {
                for (int i = 0; i < dirtyBoundsParts.Count; i++)
                    union.Encapsulate(TransformBoundsToWorld(dirtyBoundsParts[i]));
            }
            model.MarkDirtyBounds(union);
            model.RebuildDirty(); // RebuildDirty() already calls RebuildComposition() internally
        }
    }

    /// <summary>Transform a bounds from Objects-parent-local space to world space.</summary>
    Bounds TransformBoundsToWorld(Bounds local)
    {
        // Objects root is a child of this composer, so parent-local == Objects-local.
        // Use corner-based transform for accuracy with non-uniform scale / rotation.
        Vector3[] corners = new Vector3[8];
        Vector3 half = local.size * 0.5f;
        corners[0] = transform.TransformPoint(new Vector3(local.center.x - half.x, local.center.y - half.y, local.center.z - half.z));
        corners[1] = transform.TransformPoint(new Vector3(local.center.x + half.x, local.center.y - half.y, local.center.z - half.z));
        corners[2] = transform.TransformPoint(new Vector3(local.center.x - half.x, local.center.y + half.y, local.center.z - half.z));
        corners[3] = transform.TransformPoint(new Vector3(local.center.x + half.x, local.center.y + half.y, local.center.z - half.z));
        corners[4] = transform.TransformPoint(new Vector3(local.center.x - half.x, local.center.y - half.y, local.center.z + half.z));
        corners[5] = transform.TransformPoint(new Vector3(local.center.x + half.x, local.center.y - half.y, local.center.z + half.z));
        corners[6] = transform.TransformPoint(new Vector3(local.center.x - half.x, local.center.y + half.y, local.center.z + half.z));
        corners[7] = transform.TransformPoint(new Vector3(local.center.x + half.x, local.center.y + half.y, local.center.z + half.z));

        Bounds world = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < 8; i++)
            world.Encapsulate(corners[i]);
        return world;
    }
}
