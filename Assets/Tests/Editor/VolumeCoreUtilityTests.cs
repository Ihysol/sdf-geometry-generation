using NUnit.Framework;
using UnityEngine;

public class VolumeCoreUtilityTests
{
    [Test]
    public void QuantizedVector3Key_TreatsTinyCoordinateNoiseAsSamePoint()
    {
        Vector3 origin = new Vector3(-2.25f, -2.25f, -2.25f);
        Vector3 cellSize = Vector3.one * (4.5f / 64f);
        float quantum = QuantizedVector3Key.GetQuantum(cellSize);

        QuantizedVector3Key a = QuantizedVector3Key.FromPosition(
            new Vector3(1.0000001f, 0f, 0f),
            origin,
            quantum);
        QuantizedVector3Key b = QuantizedVector3Key.FromPosition(
            new Vector3(1.0000002f, 0f, 0f),
            origin,
            quantum);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void QuantizedVector3Key_SeparatesDistinctGradientSamples()
    {
        Vector3 origin = Vector3.zero;
        Vector3 cellSize = Vector3.one * 0.1f;
        float quantum = QuantizedVector3Key.GetQuantum(cellSize);

        QuantizedVector3Key a = QuantizedVector3Key.FromPosition(Vector3.zero, origin, quantum);
        QuantizedVector3Key b = QuantizedVector3Key.FromPosition(new Vector3(0.001f, 0f, 0f), origin, quantum);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void VoxelGrid_TryGetAndTrySetRejectOutOfBoundsCoordinates()
    {
        VoxelGrid grid = new VoxelGrid(new Vector3Int(2, 2, 2), Vector3.zero, Vector3.one);

        Assert.That(grid.TrySetValue(1, 1, 1, 4.25f), Is.True);
        Assert.That(grid.TryGetValue(1, 1, 1, out float value), Is.True);
        Assert.That(value, Is.EqualTo(4.25f));

        Assert.That(grid.IsInBounds(-1, 0, 0), Is.False);
        Assert.That(grid.TrySetValue(2, 0, 0, 10f), Is.False);
        Assert.That(grid.TryGetValue(0, 2, 0, out _), Is.False);
    }

    [Test]
    public void VoxelGrid_GetAndSetThrowHelpfulExceptionForOutOfBoundsCoordinates()
    {
        VoxelGrid grid = new VoxelGrid(new Vector3Int(2, 2, 2), Vector3.zero, Vector3.one);

        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.GetValue(2, 0, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.SetValue(0, -1, 0, 1f));
    }

    [Test]
    public void OctreeHermiteEdgeKey_IsDirectionIndependent()
    {
        Vector3Int a = new Vector3Int(1, 2, 3);
        Vector3Int b = new Vector3Int(4, 5, 6);

        OctreeHermiteEdgeKey forward = new OctreeHermiteEdgeKey(a, b);
        OctreeHermiteEdgeKey reverse = new OctreeHermiteEdgeKey(b, a);

        Assert.That(forward, Is.EqualTo(reverse));
        Assert.That(forward.GetHashCode(), Is.EqualTo(reverse.GetHashCode()));
    }

    [Test]
    public void FlatOctreeCrossingInvalidation_UsesInclusiveGridEdgeOverlap()
    {
        OctreeHermiteEdgeKey edge = new OctreeHermiteEdgeKey(
            new Vector3Int(4, 5, 6),
            new Vector3Int(5, 5, 6));
        Vector3Int dirtyMin = new Vector3Int(5, 4, 6);
        Vector3Int dirtyMax = new Vector3Int(8, 8, 8);

        Assert.That(FlatOctreeVolumeBuilder.CrossingEdgeOverlapsGridRange(edge, dirtyMin, dirtyMax), Is.True);
    }

    [Test]
    public void FlatOctreeCrossingInvalidation_SkipsEdgesOutsideGridRange()
    {
        OctreeHermiteEdgeKey edge = new OctreeHermiteEdgeKey(
            new Vector3Int(1, 1, 1),
            new Vector3Int(2, 1, 1));
        Vector3Int dirtyMin = new Vector3Int(5, 5, 5);
        Vector3Int dirtyMax = new Vector3Int(8, 8, 8);

        Assert.That(FlatOctreeVolumeBuilder.CrossingEdgeOverlapsGridRange(edge, dirtyMin, dirtyMax), Is.False);
    }
}
