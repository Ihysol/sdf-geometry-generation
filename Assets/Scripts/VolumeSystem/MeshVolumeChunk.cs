using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshVolumeChunk : VolumeChunkBase
{
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;

    private readonly DualContouringOctreeMesher _mesher = new();

    public override void Rebuild(
        VolumeModel model,
        IScalarFieldSource source)
    {
        EnsureSetup();

        OctreeVolumeBuilder template = model.octreeSampler.builder;

        OctreeVolumeBuilder builder = new OctreeVolumeBuilder
        {
            center = buildBounds.center,
            size = buildBounds.size,
            boundsPadding = template.boundsPadding,
            minDepth = template.minDepth,
            maxDepth = template.maxDepth
        };

        OctreeVolume volume = builder.Build(source);

        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;

        _mesher.isoLevel = model.isoLevel;
        _mesher.BuildMesh(volume, _mesh);

        ClipMeshToCoreBounds();

        if (model.recalculateNormals)
            _mesh.RecalculateNormals();

        if (model.recalculateBounds)
            _mesh.RecalculateBounds();
    }

    public override void Clear()
    {
        EnsureSetup();

        if (_mesh != null)
            _mesh.Clear();
    }

    private void EnsureSetup()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = $"Chunk Mesh {name}";
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;
        }

        if (_meshRenderer.sharedMaterial == null)
            _meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }

    private void ClipMeshToCoreBounds()
    {
        if (_mesh == null)
            return;

        Vector3[] vertices = _mesh.vertices;
        int[] oldTriangles = _mesh.triangles;

        System.Collections.Generic.List<int> newTriangles = new();

        for (int i = 0; i < oldTriangles.Length; i += 3)
        {
            int i0 = oldTriangles[i];
            int i1 = oldTriangles[i + 1];
            int i2 = oldTriangles[i + 2];

            Vector3 center =
                (vertices[i0] + vertices[i1] + vertices[i2]) / 3f;

            if (!coreBounds.Contains(center))
                continue;

            newTriangles.Add(i0);
            newTriangles.Add(i1);
            newTriangles.Add(i2);
        }

        _mesh.SetTriangles(newTriangles, 0);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.parent != null
            ? transform.parent.localToWorldMatrix
            : Matrix4x4.identity;

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
        Gizmos.DrawWireCube(coreBounds.center, coreBounds.size);

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawWireCube(buildBounds.center, buildBounds.size);

        Gizmos.matrix = Matrix4x4.identity;
    }
}