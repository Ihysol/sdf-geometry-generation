using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(SDFSampler))]
public class SDFInstancedRenderer : MonoBehaviour
{
    [Header("Rendering")]
    public Mesh instanceMesh;
    public Material instanceMaterial;
    public float instanceScale = 0.05f;

    [Header("Filtering")]
    public bool drawInside = true;
    public bool drawSurface = false;
    public float surfaceThreshold = 0.05f;

    private SDFSampler _sampler;
    private readonly List<Matrix4x4> _matrices = new();
    private readonly List<Matrix4x4[]> _batches = new();

    private void Awake()
    {
        _sampler = GetComponent<SDFSampler>();
    }

    private void OnValidate()
    {
        if (instanceScale < 0.0001f)
            instanceScale = 0.0001f;
    }

    private void Update()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();

        if (_sampler == null)
        {
            Debug.LogWarning("No SDFSampler found.");
            return;
        }

        if (instanceMesh == null)
        {
            Debug.LogWarning("No instanceMesh assigned.");
            return;
        }

        if (instanceMaterial == null)
        {
            Debug.LogWarning("No instanceMaterial assigned.");
            return;
        }

        _sampler.RebuildSamples();
        RebuildMatrices();

        Debug.Log($"Samples: {_sampler.Samples?.Length ?? 0}, Matrices: {_matrices.Count}, Batches: {_batches.Count}");

        DrawBatches();

    }

    private void RebuildMatrices()
    {
        // clear old data
        _matrices.Clear();
        _batches.Clear();

        // check for samples
        if (_sampler.Samples == null)
            return;

        // uniform scaling
        Vector3 scale = Vector3.one * instanceScale;

        // iterate over samples
        foreach (SDFSample sample in _sampler.Samples)
        {
            // classify sample
            bool isSurface = Mathf.Abs(sample.Distance) <= surfaceThreshold;
            bool isInside = sample.Distance < 0f;

            // decide if point should be drawn (NOT mutually exclusive anymore)
            bool shouldDraw =
                (drawSurface && isSurface) ||
                (drawInside && isInside);

            if (!shouldDraw)
                continue;

            // store translation rotation matrix in _matrices
            _matrices.Add(Matrix4x4.TRS(sample.WorldPosition, Quaternion.identity, scale));
        }

        // divide matrices into batches
        // LIMITATION: Graphics.DrawMeshInstanced(...) can only draw 1023 instances per call.
        for (int i = 0; i < _matrices.Count; i += 1023)
        {
            int count = Mathf.Min(1023, _matrices.Count - i); // get current batch size
            Matrix4x4[] batch = new Matrix4x4[count]; // create array for batch

            for (int j = 0; j < count; j++)
            {
                batch[j] = _matrices[i + j]; // copy matrices into batch
            }

            _batches.Add(batch); // add batches to data structure
        }
    }

    private void DrawBatches()
    {
        // draw call for each batch (limit 1023 per call)
        foreach (Matrix4x4[] batch in _batches)
        {
            Graphics.DrawMeshInstanced(
                instanceMesh,
                0,
                instanceMaterial,
                batch,
                batch.Length,
                null,
                ShadowCastingMode.Off,
                false,
                gameObject.layer
            );
        }
    }

    private void OnDrawGizmos()
    {
        // draw grid outline
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, _sampler.gridExtent);
    }

}