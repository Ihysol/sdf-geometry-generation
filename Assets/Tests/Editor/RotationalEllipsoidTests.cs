using NUnit.Framework;
using UnityEngine;

public class RotationalEllipsoidTests
{
    [Test]
    public void RotationalEllipsoidSDF_EvaluatesAxesAndInterior()
    {
        RotationalEllipsoidSDF ellipsoid = ScriptableObject.CreateInstance<RotationalEllipsoidSDF>();
        ellipsoid.radialRadius = 3f;
        ellipsoid.verticalRadius = 1.5f;

        try
        {
            Assert.That(ellipsoid.Evaluate(new Vector3(3f, 0f, 0f)), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ellipsoid.Evaluate(new Vector3(0f, 0f, 3f)), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ellipsoid.Evaluate(new Vector3(0f, 1.5f, 0f)), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(ellipsoid.Evaluate(Vector3.zero), Is.LessThan(0f));
            Assert.That(ellipsoid.Evaluate(new Vector3(3.5f, 0f, 0f)), Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(ellipsoid);
        }
    }

    [Test]
    public void RotationalEllipsoidGridCutter_CutsProfileAngleLines()
    {
        RotationalEllipsoidSDF ellipsoid = ScriptableObject.CreateInstance<RotationalEllipsoidSDF>();
        RotationalEllipsoidGridCutter cutter = ScriptableObject.CreateInstance<RotationalEllipsoidGridCutter>();
        ellipsoid.radialRadius = 2f;
        ellipsoid.verticalRadius = 1f;
        cutter.radialRadius = 2f;
        cutter.verticalRadius = 1f;
        cutter.profileAngleSegments = 4;
        cutter.width = 0.02f;
        cutter.depth = 0.1f;

        try
        {
            Vector3 onNinetyDegreeLine = new Vector3(2f, 0f, 0f);
            Vector3 betweenLines = PointOnEllipsoidProfile(2f, 1f, Mathf.PI * 0.125f);

            Assert.That(cutter.Evaluate(onNinetyDegreeLine, ellipsoid), Is.LessThan(0f));
            Assert.That(cutter.Evaluate(betweenLines, ellipsoid), Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(cutter);
            Object.DestroyImmediate(ellipsoid);
        }
    }

    [Test]
    public void Snapshot_EvaluatesRotationalEllipsoidLikeVolumeObject()
    {
        GameObject root = new GameObject("root");
        GameObject child = new GameObject("ellipsoid");
        child.transform.SetParent(root.transform, false);

        try
        {
            VolumeObject volumeObject = child.AddComponent<VolumeObject>();
            volumeObject.shapeType = VolumeShapeType.RotationalEllipsoid;
            volumeObject.ellipsoidRadialRadius = 2f;
            volumeObject.ellipsoidVerticalRadius = 1f;
            volumeObject.gridType = VolumeGridType.RotationalEllipsoid;
            volumeObject.gridWidth = 0.02f;
            volumeObject.gridDepth = 0.1f;
            volumeObject.ellipsoidProfileAngleSegments = 4;

            SdfSceneSnapshot snapshot = new SdfSceneSnapshot(root.transform, new() { volumeObject });
            Vector3 sample = new Vector3(2f, 0f, 0f);

            Assert.That(snapshot.HasUnsupportedShapes, Is.False);
            Assert.That(snapshot.Evaluate(sample), Is.EqualTo(volumeObject.EvaluateLocal(sample)).Within(1e-5f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Vector3 PointOnEllipsoidProfile(float radialRadius, float verticalRadius, float angle)
    {
        return new Vector3(
            radialRadius * Mathf.Sin(angle),
            verticalRadius * Mathf.Cos(angle),
            0f
        );
    }
}
