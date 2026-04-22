using UnityEngine;

[RequireComponent(typeof(SDFSampler))]
public class SDFGizmoRenderer : MonoBehaviour
{
    [Header("Settings")]
    public float pointSize = 0.05f;
    [Header("Debug")]
    public bool drawInside = false;
    public bool drawSurface = true;
    public bool drawOutside = false;
    public float surfaceThreshold = 0.05f;


    private SDFSampler _sampler;

    private void OnDrawGizmos()
    {
        _sampler = GetComponent<SDFSampler>();
        if (_sampler == null)
        {
            return;
        }

        _sampler.RebuildSamples();

        if (_sampler.Samples == null)
        {
            return;
        }

        // draw gizmos samples
        foreach (SDFSample sample in _sampler.Samples)
        {
            if (drawSurface && Mathf.Abs(sample.Distance) <= surfaceThreshold)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawCube(sample.LocalPosition, new Vector3(pointSize, pointSize, pointSize));
            }
            else if (drawInside && sample.Distance < 0f)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawCube(sample.LocalPosition, new Vector3(pointSize, pointSize, pointSize));
            }
            else if (drawOutside)
            {
                Gizmos.color = Color.lightGray;
                Gizmos.DrawCube(sample.LocalPosition, new Vector3(pointSize, pointSize, pointSize));
            }
        }

        // draw grid outline
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, _sampler.gridExtent);



    }
}