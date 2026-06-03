using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class SceneCompositeSDFTests
{
    [Test]
    public void Evaluate_AppliesGroupedOperationsAndCachedTransforms()
    {
        GameObject root = new GameObject("root");
        GameObject addObject = CreateSphere(root.transform, VolumeOperationRole.Add, Vector3.zero, 2f);
        GameObject subtractObject = CreateSphere(root.transform, VolumeOperationRole.Subtract, Vector3.zero, 0.5f);
        GameObject intersectObject = CreateSphere(root.transform, VolumeOperationRole.Intersect, new Vector3(0.5f, 0f, 0f), 1f);

        try
        {
            List<VolumeObject> objects = new()
            {
                subtractObject.GetComponent<VolumeObject>(),
                intersectObject.GetComponent<VolumeObject>(),
                addObject.GetComponent<VolumeObject>()
            };
            SceneCompositeSDF composite = new SceneCompositeSDF(root.transform, objects);

            Assert.That(composite.Evaluate(Vector3.zero), Is.EqualTo(0.5f).Within(1e-5f));

            addObject.transform.localPosition = Vector3.one * 10f;
            Assert.That(composite.Evaluate(Vector3.zero), Is.EqualTo(0.5f).Within(1e-5f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreateSphere(
        Transform parent,
        VolumeOperationRole role,
        Vector3 localPosition,
        float radius)
    {
        GameObject child = new GameObject(role.ToString());
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.role = role;
        volumeObject.shapeType = VolumeShapeType.Sphere;
        volumeObject.sphereRadius = radius;
        return child;
    }
}
