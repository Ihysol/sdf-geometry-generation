using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class BurstSdfSceneSnapshotTests
{
    private const float Tolerance = 1e-5f;

    [BurstCompile]
    private struct EvaluateJob : IJob
    {
        [ReadOnly] public BurstSdfSceneView Scene;
        public float3 Point;
        public NativeArray<float> Result;

        public void Execute()
        {
            Result[0] = Scene.Evaluate(Point);
        }
    }

    [Test]
    public void Snapshot_IsNonCopyableOwner()
    {
        Assert.That(typeof(BurstSdfSceneSnapshot).IsClass, Is.True);
        Assert.That(typeof(BurstSdfSceneSnapshot).IsSealed, Is.True);
    }

    [TestCase(VolumeShapeType.Sphere)]
    [TestCase(VolumeShapeType.Box)]
    [TestCase(VolumeShapeType.Torus)]
    [TestCase(VolumeShapeType.Hyperboloid)]
    public void Evaluate_MatchesManagedSnapshotForTransformedPrimitive(VolumeShapeType shapeType)
    {
        GameObject root = new GameObject("root");
        root.transform.SetPositionAndRotation(new Vector3(1.5f, -0.75f, 2f), Quaternion.Euler(10f, 25f, -15f));
        root.transform.localScale = new Vector3(1.2f, 0.8f, 1.1f);
        VolumeObject shape = CreateShape(root.transform, shapeType, VolumeOperationRole.Add);
        shape.transform.localPosition = new Vector3(0.35f, -0.2f, 0.6f);
        shape.transform.localRotation = Quaternion.Euler(-20f, 35f, 12f);
        shape.transform.localScale = new Vector3(0.75f, 1.4f, 0.9f);

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.Temp, out burst), Is.True);
                float3[] samples =
                {
                    new float3(0f, 0f, 0f),
                    new float3(0.4f, -0.15f, 0.7f),
                    new float3(-0.8f, 0.55f, 1.1f)
                };

                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 sample = new Vector3(samples[i].x, samples[i].y, samples[i].z);
                    Assert.That(burst.Evaluate(samples[i]), Is.EqualTo(managed.Evaluate(sample)).Within(Tolerance));
                }
            }
            finally
            {
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Evaluate_MatchesManagedSnapshotForGroupedOperations()
    {
        GameObject root = new GameObject("root");
        VolumeObject add = CreateShape(root.transform, VolumeShapeType.Box, VolumeOperationRole.Add);
        add.boxHalfExtents = new Vector3(1.8f, 1.25f, 1.5f);
        VolumeObject subtract = CreateShape(root.transform, VolumeShapeType.Sphere, VolumeOperationRole.Subtract);
        subtract.sphereRadius = 0.65f;
        subtract.transform.localPosition = new Vector3(0.25f, 0f, 0f);
        VolumeObject intersect = CreateShape(root.transform, VolumeShapeType.Torus, VolumeOperationRole.Intersect);
        intersect.torusMajorRadius = 0.9f;
        intersect.torusMinorRadius = 0.45f;

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, subtract, intersect, add);
            BurstSdfSceneSnapshot burst = null;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.Temp, out burst), Is.True);
                float3[] samples =
                {
                    new float3(0f, 0f, 0f),
                    new float3(0.7f, 0.2f, 0f),
                    new float3(1.3f, -0.1f, 0.35f)
                };

                for (int i = 0; i < samples.Length; i++)
                {
                    float3 sample = samples[i];
                    Assert.That(
                        burst.Evaluate(sample),
                        Is.EqualTo(managed.Evaluate(new Vector3(sample.x, sample.y, sample.z))).Within(Tolerance));
                }
            }
            finally
            {
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void View_EvaluatesInsideBurstCompiledJob()
    {
        GameObject root = new GameObject("root");
        VolumeObject shape = CreateShape(root.transform, VolumeShapeType.Torus, VolumeOperationRole.Add);
        shape.transform.localPosition = new Vector3(0.3f, -0.2f, 0.5f);
        float3 sample = new float3(0.8f, 0.15f, -0.25f);

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            NativeArray<float> result = default;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.TempJob, out burst), Is.True);
                result = new NativeArray<float>(1, Allocator.TempJob);
                EvaluateJob job = new EvaluateJob
                {
                    Scene = burst.View,
                    Point = sample,
                    Result = result
                };

                job.Schedule().Complete();

                Assert.That(
                    result[0],
                    Is.EqualTo(managed.Evaluate(new Vector3(sample.x, sample.y, sample.z))).Within(Tolerance));
            }
            finally
            {
                if (result.IsCreated)
                    result.Dispose();
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryCreate_RejectsNullAndUnsupportedSnapshots()
    {
        Assert.That(BurstSdfSceneSnapshot.TryCreate(null, Allocator.Temp, out BurstSdfSceneSnapshot nullResult), Is.False);
        Assert.That(nullResult, Is.Null);

        GameObject root = new GameObject("root");
        VolumeObject custom = CreateShape(root.transform, VolumeShapeType.CustomAsset, VolumeOperationRole.Add);
        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, custom);
            Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.Temp, out BurstSdfSceneSnapshot unsupported), Is.False);
            Assert.That(unsupported, Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [TestCase(VolumeShapeType.Sphere, VolumeGridType.Global)]
    [TestCase(VolumeShapeType.Box, VolumeGridType.Sphere)]
    [TestCase(VolumeShapeType.Torus, VolumeGridType.Torus)]
    [TestCase(VolumeShapeType.Hyperboloid, VolumeGridType.Hyperboloid)]
    public void Evaluate_MatchesManagedSnapshotForGridCutter(VolumeShapeType shapeType, VolumeGridType gridType)
    {
        GameObject root = new GameObject("root");
        root.transform.SetPositionAndRotation(new Vector3(0.5f, 0f, -1f), Quaternion.Euler(8f, 30f, 0f));
        root.transform.localScale = new Vector3(1f, 1f, 1f);

        VolumeObject shape = CreateShape(root.transform, shapeType, VolumeOperationRole.Add);
        shape.transform.localPosition = new Vector3(0.2f, 0.3f, -0.4f);
        shape.gridType = gridType;
        shape.gridWidth = 0.08f;
        shape.gridDepth = 0.15f;
        shape.gridSpacing = new Vector3(0.6f, 0.7f, 0.5f);
        shape.gridOffset = new Vector3(0.1f, 0.2f, 0.05f);
        shape.globalGridInWorldSpace = true;
        shape.useXLines = true;
        shape.useYLines = true;
        shape.useZLines = false;
        shape.longitudeCount = 8;
        shape.latitudeCount = 6;
        shape.torusMajorSegments = 12;
        shape.torusMinorSegments = 8;
        shape.hyperboloidRadialSegments = 10;
        shape.hyperboloidHeightSegments = 6;
        shape.hyperboloidHeightMin = -1.5f;
        shape.hyperboloidHeightMax = 1.5f;

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.Temp, out burst), Is.True);
                float3[] samples =
                {
                    new float3(0f, 0f, 0f),
                    new float3(0.5f, 0.3f, -0.2f),
                    new float3(-0.7f, 0.6f, 0.8f),
                    new float3(1f, -0.5f, -1f)
                };

                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 sample = new Vector3(samples[i].x, samples[i].y, samples[i].z);
                    float managedVal = managed.Evaluate(sample);
                    float burstVal = burst.Evaluate(samples[i]);

                    Assert.That(
                        burstVal,
                        Is.EqualTo(managedVal).Within(2e-5f),
                        $"Shape={shapeType}, Grid={gridType}, Point={sample}");
                }
            }
            finally
            {
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [TestCase(31, false)]
    [TestCase(32, true)]
    public void EvaluateBatch_UsesCorrectBranch(int count, bool expectedJob)
    {
        GameObject root = new GameObject("root");
        VolumeObject shape = CreateShape(root.transform, VolumeShapeType.Sphere, VolumeOperationRole.Add);

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            NativeArray<float3> positions = default;
            NativeArray<float> values = default;
            NativeArray<float> managedValues = default;

            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.TempJob, out burst), Is.True);

                positions = new NativeArray<float3>(count, Allocator.TempJob);
                values = new NativeArray<float>(count, Allocator.TempJob);
                managedValues = new NativeArray<float>(count, Allocator.TempJob);

                for (int i = 0; i < count; i++)
                    positions[i] = new float3(i * 0.1f, i * 0.05f, i * 0.07f);

                BurstSdfBatchResult result = burst.EvaluateBatch(positions, values, 32);

                Assert.That(result.UsedJob, Is.EqualTo(expectedJob));
                Assert.That(result.SampleCount, Is.EqualTo(count));

                for (int i = 0; i < count; i++)
                {
                    managedValues[i] = managed.Evaluate(new Vector3(positions[i].x, positions[i].y, positions[i].z));
                    Assert.That(values[i], Is.EqualTo(managedValues[i]).Within(Tolerance));
                }
            }
            finally
            {
                if (positions.IsCreated) positions.Dispose();
                if (values.IsCreated) values.Dispose();
                if (managedValues.IsCreated) managedValues.Dispose();
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void EvaluateBatch_ThrowsOnMismatchedLengths()
    {
        GameObject root = new GameObject("root");
        VolumeObject shape = CreateShape(root.transform, VolumeShapeType.Sphere, VolumeOperationRole.Add);

        try
        {
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.TempJob, out burst), Is.True);

                using (NativeArray<float3> positions = new NativeArray<float3>(5, Allocator.TempJob))
                using (NativeArray<float> values = new NativeArray<float>(3, Allocator.TempJob))
                {
                    Assert.Throws<System.InvalidOperationException>(() => burst.EvaluateBatch(positions, values, 32));
                }
            }
            finally
            {
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryCreate_CopiesShapeAndGridFieldsAndDisposeClearsCreationState()
    {
        GameObject root = new GameObject("root");
        VolumeObject shape = CreateShape(root.transform, VolumeShapeType.Hyperboloid, VolumeOperationRole.Add);
        root.transform.SetPositionAndRotation(new Vector3(2f, -1f, 0.5f), Quaternion.Euler(12f, 23f, 34f));
        root.transform.localScale = new Vector3(1.1f, 0.9f, 1.3f);
        shape.transform.localPosition = new Vector3(0.4f, 0.5f, -0.6f);
        shape.transform.localRotation = Quaternion.Euler(-11f, 31f, 7f);
        shape.transform.localScale = new Vector3(0.7f, 1.4f, 0.8f);
        shape.sphereRadius = 1.23f;
        shape.boxHalfExtents = new Vector3(2.1f, 2.2f, 2.3f);
        shape.torusMajorRadius = 3.4f;
        shape.torusMinorRadius = 0.56f;
        shape.hyperboloidA = 0.67f;
        shape.hyperboloidB = 0.78f;
        shape.hyperboloidC = 0.89f;
        shape.gridType = VolumeGridType.Hyperboloid;
        shape.gridWidth = 0.12f;
        shape.gridDepth = 0.34f;
        shape.gridSpacing = new Vector3(1.1f, 1.2f, 1.3f);
        shape.gridOffset = new Vector3(0.1f, 0.2f, 0.3f);
        shape.globalGridInWorldSpace = true;
        shape.longitudeCount = 11;
        shape.latitudeCount = 12;
        shape.torusMajorSegments = 13;
        shape.torusMinorSegments = 14;
        shape.hyperboloidRadialSegments = 15;
        shape.hyperboloidHeightSegments = 16;
        shape.hyperboloidHeightMin = -1.7f;
        shape.hyperboloidHeightMax = 2.3f;
        shape.useXLines = true;
        shape.useYLines = false;
        shape.useZLines = true;

        try
        {
            Matrix4x4 expectedRootLocalToWorld = root.transform.localToWorldMatrix;
            Matrix4x4 expectedWorldToLocal = shape.transform.worldToLocalMatrix;
            Matrix4x4 expectedLocalToWorld = shape.transform.localToWorldMatrix;
            SdfSceneSnapshot managed = CreateSnapshot(root, shape);
            BurstSdfSceneSnapshot burst = null;
            try
            {
                Assert.That(BurstSdfSceneSnapshot.TryCreate(managed, Allocator.Temp, out burst), Is.True);
                Assert.That(burst.IsCreated, Is.True);
                Assert.That(burst.AddShapes.Length, Is.EqualTo(1));

                BurstSdfShapeData copied = burst.AddShapes[0];
                AssertMatrix(burst.View.RootLocalToWorld, expectedRootLocalToWorld);
                AssertMatrix(copied.WorldToLocal, expectedWorldToLocal);
                AssertMatrix(copied.LocalToWorld, expectedLocalToWorld);
                Assert.That(copied.ShapeType, Is.EqualTo((int)VolumeShapeType.Hyperboloid));
                Assert.That(copied.SphereRadius, Is.EqualTo(1.23f).Within(Tolerance));
                Assert.That(copied.BoxHalfExtents, Is.EqualTo(new float3(2.1f, 2.2f, 2.3f)));
                Assert.That(copied.TorusMajorRadius, Is.EqualTo(3.4f).Within(Tolerance));
                Assert.That(copied.TorusMinorRadius, Is.EqualTo(0.56f).Within(Tolerance));
                Assert.That(copied.HyperboloidA, Is.EqualTo(0.67f).Within(Tolerance));
                Assert.That(copied.HyperboloidB, Is.EqualTo(0.78f).Within(Tolerance));
                Assert.That(copied.HyperboloidC, Is.EqualTo(0.89f).Within(Tolerance));
                Assert.That(copied.GridType, Is.EqualTo((int)VolumeGridType.Hyperboloid));
                Assert.That(copied.GridWidth, Is.EqualTo(0.12f).Within(Tolerance));
                Assert.That(copied.GridDepth, Is.EqualTo(0.34f).Within(Tolerance));
                Assert.That(copied.GridSpacing, Is.EqualTo(new float3(1.1f, 1.2f, 1.3f)));
                Assert.That(copied.GridOffset, Is.EqualTo(new float3(0.1f, 0.2f, 0.3f)));
                Assert.That(copied.GlobalGridInWorldSpace, Is.EqualTo(1));
                Assert.That(copied.LongitudeCount, Is.EqualTo(11));
                Assert.That(copied.LatitudeCount, Is.EqualTo(12));
                Assert.That(copied.TorusMajorSegments, Is.EqualTo(13));
                Assert.That(copied.TorusMinorSegments, Is.EqualTo(14));
                Assert.That(copied.HyperboloidRadialSegments, Is.EqualTo(15));
                Assert.That(copied.HyperboloidHeightSegments, Is.EqualTo(16));
                Assert.That(copied.HyperboloidHeightMin, Is.EqualTo(-1.7f).Within(Tolerance));
                Assert.That(copied.HyperboloidHeightMax, Is.EqualTo(2.3f).Within(Tolerance));
                Assert.That(copied.UseXLines, Is.EqualTo(1));
                Assert.That(copied.UseYLines, Is.Zero);
                Assert.That(copied.UseZLines, Is.EqualTo(1));

                burst.Dispose();
                Assert.That(burst.IsCreated, Is.False);
                Assert.DoesNotThrow(() => burst.Dispose());
            }
            finally
            {
                burst?.Dispose();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void AssertMatrix(float4x4 actual, Matrix4x4 expected)
    {
        Assert.That(actual.c0, Is.EqualTo(new float4(expected.m00, expected.m10, expected.m20, expected.m30)));
        Assert.That(actual.c1, Is.EqualTo(new float4(expected.m01, expected.m11, expected.m21, expected.m31)));
        Assert.That(actual.c2, Is.EqualTo(new float4(expected.m02, expected.m12, expected.m22, expected.m32)));
        Assert.That(actual.c3, Is.EqualTo(new float4(expected.m03, expected.m13, expected.m23, expected.m33)));
    }

    private static SdfSceneSnapshot CreateSnapshot(GameObject root, params VolumeObject[] shapes)
    {
        return new SdfSceneSnapshot(root.transform, new List<VolumeObject>(shapes));
    }

    private static VolumeObject CreateShape(
        Transform parent,
        VolumeShapeType shapeType,
        VolumeOperationRole role)
    {
        GameObject child = new GameObject(shapeType.ToString());
        child.transform.SetParent(parent, false);
        VolumeObject shape = child.AddComponent<VolumeObject>();
        shape.shapeType = shapeType;
        shape.role = role;
        shape.sphereRadius = 1.15f;
        shape.boxHalfExtents = new Vector3(0.8f, 1.1f, 0.6f);
        shape.torusMajorRadius = 0.9f;
        shape.torusMinorRadius = 0.3f;
        shape.hyperboloidA = 0.75f;
        shape.hyperboloidB = 1.2f;
        shape.hyperboloidC = 0.85f;
        return shape;
    }
}
