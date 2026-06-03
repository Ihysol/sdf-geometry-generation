using NUnit.Framework;
using UnityEngine;

public class DualContouringOctreeMesherCacheTests
{
    [Test]
    public void BuildMesh_ReusesGhostSamplesForSameVolume()
    {
        CountingSphereSource source = new CountingSphereSource();
        OctreeVolumeBuilder builder = new OctreeVolumeBuilder
        {
            center = Vector3.zero,
            size = Vector3.one * 2f,
            boundsPadding = 0f,
            minDepth = 1,
            maxDepth = 3,
            suppressBuildLog = true
        };
        OctreeVolume volume = builder.Build(source);
        DualContouringOctreeMesher mesher = new DualContouringOctreeMesher
        {
            ownedBounds = new Bounds(new Vector3(-0.5f, 0f, 0f), new Vector3(1f, 2f, 2f))
        };
        Mesh mesh = new Mesh();

        source.Reset();
        mesher.BuildMesh(volume, 0f, mesh);
        int firstBuildEvaluations = source.Evaluations;

        source.Reset();
        mesher.BuildMesh(volume, 0f, mesh);
        int secondBuildEvaluations = source.Evaluations;

        Object.DestroyImmediate(mesh);

        Assert.That(firstBuildEvaluations, Is.GreaterThan(0));
        Assert.That(secondBuildEvaluations, Is.LessThan(firstBuildEvaluations));
    }

    private sealed class CountingSphereSource : IScalarFieldSource
    {
        public int Evaluations { get; private set; }

        public float Evaluate(Vector3 worldPosition)
        {
            Evaluations++;
            return worldPosition.magnitude - 0.75f;
        }

        public void Reset()
        {
            Evaluations = 0;
        }
    }

    [Test]
    public void FlatLayout_RuntimeCacheMapsContainedCellsToLeaf()
    {
        FlatOctreeLayout layout = new FlatOctreeLayout
        {
            Centers = new[] { Vector3.zero },
            Sizes = new[] { Vector3.one },
            SurfaceVertices = new[] { Vector3.zero },
            Coords = new[] { Vector3Int.zero },
            NodeSizeInCells = new[] { new Vector3Int(4, 4, 4) },
            CornerValues8 = new float[8],
            FirstChildIndex = new[] { -1 },
            ChildMask = new byte[] { 0 },
            Flags = new[] { (byte)(FlatOctreeLayout.FlagLeaf | FlatOctreeLayout.FlagSurface) }
        };

        layout.EnsureRuntimeCache();

        Assert.That(layout.TryGetContainingLeafIndex(new Vector3Int(0, 0, 0), out int originLeaf), Is.True);
        Assert.That(originLeaf, Is.EqualTo(0));
        Assert.That(layout.TryGetContainingLeafIndex(new Vector3Int(3, 3, 3), out int farLeaf), Is.True);
        Assert.That(farLeaf, Is.EqualTo(0));
        Assert.That(layout.TryGetContainingLeafIndex(new Vector3Int(4, 0, 0), out _), Is.False);
    }
}
