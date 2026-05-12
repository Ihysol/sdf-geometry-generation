using System.Collections.Generic;
using UnityEngine;

public class MeshData
{
    public readonly List<Vector3> Vertices = new();
    public readonly List<int> Triangles = new();

    public Bounds Bounds;

    /// <summary>Removes all vertices, triangles, and cached bounds.</summary>
    public void Clear()
    {
        Vertices.Clear();
        Triangles.Clear();

        Bounds = new Bounds(Vector3.zero, Vector3.zero);
    }

    /// <summary>Creates a Unity mesh from the stored buffers.</summary>
    public Mesh ToMesh(bool recalculateNormals = true)
    {
        Mesh mesh = new Mesh();

        mesh.name = "Generated Volume Mesh";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(Vertices);
        mesh.SetTriangles(Triangles, 0);

        if (recalculateNormals)
            mesh.RecalculateNormals();

        mesh.bounds = Bounds;

        return mesh;
    }
}
