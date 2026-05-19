using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Independent octree mesher using marching tetrahedra on the finest grid.
/// </summary>
public class DualMarchingTetrahedraOctreeMesher : IVolumeMesher<OctreeVolume>
{
    public Bounds? ownedBounds;
    public List<Bounds> ownedBoundsList;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();
    private readonly Dictionary<Vector3Int, float> _cornerCache = new();

    private static readonly Vector3Int[] CellCornerOffsets =
    {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        new Vector3Int(1, 1, 1),
        new Vector3Int(0, 1, 1)
    };

    private static readonly int[,] Tetrahedra =
    {
        { 0, 5, 1, 6 },
        { 0, 1, 2, 6 },
        { 0, 2, 3, 6 },
        { 0, 3, 7, 6 },
        { 0, 7, 4, 6 },
        { 0, 4, 5, 6 }
    };

    private static readonly int[,] TetraEdges =
    {
        { 0, 1 }, { 1, 2 }, { 2, 0 }, { 0, 3 }, { 1, 3 }, { 2, 3 }
    };

    public void BuildMesh(OctreeVolume volume, float isoLevel, Mesh targetMesh)
    {
        targetMesh.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _cornerCache.Clear();

        if (volume == null || volume.Source == null)
            return;

        int resolution = 1 << Mathf.Max(0, volume.MaxDepth);
        Vector3 origin = volume.GridOrigin;
        Vector3 cell = volume.CellSize;
        IScalarFieldSource source = volume.Source;

        for (int z = 0; z < resolution; z++)
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            Vector3Int c = new Vector3Int(x, y, z);
            if (!IsOwnedCell(c, origin, cell))
                continue;
            PolygonizeCell(c, origin, cell, source, isoLevel);
        }

        targetMesh.SetVertices(_vertices);
        targetMesh.SetTriangles(_triangles, 0);
    }

    private bool IsOwnedCell(Vector3Int c, Vector3 origin, Vector3 cell)
    {
        if (!ownedBounds.HasValue && ownedBoundsList == null)
            return true;

        Vector3 center = origin + new Vector3((c.x + 0.5f) * cell.x, (c.y + 0.5f) * cell.y, (c.z + 0.5f) * cell.z);
        if (ownedBounds.HasValue && ContainsHalfOpen(ownedBounds.Value, center))
            return true;
        if (ownedBoundsList != null)
            for (int i = 0; i < ownedBoundsList.Count; i++)
                if (ContainsHalfOpen(ownedBoundsList[i], center))
                    return true;
        return false;
    }

    private static bool ContainsHalfOpen(Bounds b, Vector3 p)
    {
        Vector3 min = b.min;
        Vector3 max = b.max;
        return p.x >= min.x && p.x < max.x && p.y >= min.y && p.y < max.y && p.z >= min.z && p.z < max.z;
    }

    private void PolygonizeCell(Vector3Int cellCoord, Vector3 origin, Vector3 cell, IScalarFieldSource source, float iso)
    {
        Vector3[] p = new Vector3[8];
        float[] v = new float[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3Int gc = cellCoord + CellCornerOffsets[i];
            p[i] = origin + new Vector3(gc.x * cell.x, gc.y * cell.y, gc.z * cell.z);
            v[i] = SampleCorner(source, gc, p[i]);
        }

        for (int t = 0; t < 6; t++)
        {
            int a = Tetrahedra[t, 0];
            int b = Tetrahedra[t, 1];
            int c = Tetrahedra[t, 2];
            int d = Tetrahedra[t, 3];
            PolygonizeTetra(p[a], p[b], p[c], p[d], v[a], v[b], v[c], v[d], iso);
        }
    }

    private void PolygonizeTetra(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float v0, float v1, float v2, float v3, float iso)
    {
        Vector3[] p = { p0, p1, p2, p3 };
        float[] v = { v0, v1, v2, v3 };
        int mask = 0;
        if (v0 <= iso) mask |= 1;
        if (v1 <= iso) mask |= 2;
        if (v2 <= iso) mask |= 4;
        if (v3 <= iso) mask |= 8;
        if (mask == 0 || mask == 15)
            return;

        Vector3 E(int ei)
        {
            int a = TetraEdges[ei, 0];
            int b = TetraEdges[ei, 1];
            return Interpolate(p[a], p[b], v[a], v[b], iso);
        }

        switch (mask)
        {
            case 1: AddTriangle(E(0), E(2), E(3)); break;
            case 2: AddTriangle(E(0), E(4), E(1)); break;
            case 3: AddTriangle(E(1), E(2), E(3)); AddTriangle(E(1), E(3), E(4)); break;
            case 4: AddTriangle(E(1), E(5), E(2)); break;
            case 5: AddTriangle(E(0), E(1), E(3)); AddTriangle(E(1), E(5), E(3)); break;
            case 6: AddTriangle(E(0), E(5), E(2)); AddTriangle(E(0), E(4), E(5)); break;
            case 7: AddTriangle(E(3), E(4), E(5)); break;
            case 8: AddTriangle(E(3), E(5), E(4)); break;
            case 9: AddTriangle(E(0), E(5), E(4)); AddTriangle(E(0), E(2), E(5)); break;
            case 10: AddTriangle(E(0), E(3), E(1)); AddTriangle(E(1), E(3), E(5)); break;
            case 11: AddTriangle(E(1), E(2), E(5)); break;
            case 12: AddTriangle(E(1), E(4), E(3)); AddTriangle(E(1), E(3), E(2)); break;
            case 13: AddTriangle(E(0), E(1), E(4)); break;
            case 14: AddTriangle(E(0), E(3), E(2)); break;
        }
    }

    private static Vector3 Interpolate(Vector3 a, Vector3 b, float va, float vb, float iso)
    {
        float da = va - iso;
        float db = vb - iso;
        float denom = da - db;
        if (Mathf.Abs(denom) < 1e-8f)
            return (a + b) * 0.5f;
        return Vector3.Lerp(a, b, Mathf.Clamp01(da / denom));
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        int i = _vertices.Count;
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        _triangles.Add(i);
        _triangles.Add(i + 1);
        _triangles.Add(i + 2);
    }

    private float SampleCorner(IScalarFieldSource source, Vector3Int gc, Vector3 worldPos)
    {
        if (_cornerCache.TryGetValue(gc, out float cached))
            return cached;
        float value = source.Evaluate(worldPos);
        _cornerCache[gc] = value;
        return value;
    }
}
