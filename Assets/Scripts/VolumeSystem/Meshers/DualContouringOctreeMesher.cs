using System.Collections.Generic;
using UnityEngine;

public class DualContouringOctreeMesher
{
    private readonly List<Vector3> vertices = new();
    private readonly List<int> triangles = new();

    public void BuildMesh(OctreeVolume volume, Mesh mesh)
    {
        mesh.Clear();

        vertices.Clear();
        triangles.Clear();

        AddCube(volume.Bounds);

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log($"Octree mesher built cube. Vertices: {vertices.Count}");
    }

    private void AddCube(Bounds bounds)
    {
        int start = vertices.Count;

        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;

        vertices.Add(c + new Vector3(-e.x, -e.y, -e.z));
        vertices.Add(c + new Vector3( e.x, -e.y, -e.z));
        vertices.Add(c + new Vector3( e.x, -e.y,  e.z));
        vertices.Add(c + new Vector3(-e.x, -e.y,  e.z));

        vertices.Add(c + new Vector3(-e.x,  e.y, -e.z));
        vertices.Add(c + new Vector3( e.x,  e.y, -e.z));
        vertices.Add(c + new Vector3( e.x,  e.y,  e.z));
        vertices.Add(c + new Vector3(-e.x,  e.y,  e.z));

        AddQuad(start + 0, start + 1, start + 2, start + 3);
        AddQuad(start + 4, start + 7, start + 6, start + 5);
        AddQuad(start + 0, start + 4, start + 5, start + 1);
        AddQuad(start + 1, start + 5, start + 6, start + 2);
        AddQuad(start + 2, start + 6, start + 7, start + 3);
        AddQuad(start + 3, start + 7, start + 4, start + 0);
    }

    private void AddQuad(int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);

        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);
    }
}