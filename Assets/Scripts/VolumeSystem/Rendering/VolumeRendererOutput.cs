using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(VolumeMeshRenderer))]
[RequireComponent(typeof(ChunkedVolumeRenderer))]
public class VolumeRenderOutput : MonoBehaviour
{
    private VolumeMeshRenderer _single;
    private ChunkedVolumeRenderer _chunked;
    private MeshRenderer _meshRenderer;
    private MeshFilter _meshFilter;

    /// <summary>Fetches the renderers and host mesh components.</summary>
    private void EnsureSetup()
    {
        if (_single == null)
            _single = GetComponent<VolumeMeshRenderer>();

        if (_chunked == null)
            _chunked = GetComponent<ChunkedVolumeRenderer>();

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
    }

    /// <summary>Routes rebuilds to either the single-mesh or chunked renderer.</summary>
    public void Rebuild(VolumeModel model)
    {
        EnsureSetup();

        switch (model.renderMode)
        {
            case VolumeRenderMode.SingleMesh:
                RebuildSingle(model);
                break;

            case VolumeRenderMode.Chunked:
                RebuildChunked(model);
                break;
        }
    }

    /// <summary>Clears both render modes and detaches the host mesh.</summary>
    public void Clear()
    {
        EnsureSetup();

        _single.Clear();
        _chunked.ClearChunks();

        if (_meshFilter != null)
            _meshFilter.sharedMesh = null;
    }

    /// <summary>Enables and rebuilds the single-mesh renderer.</summary>
    private void RebuildSingle(VolumeModel model)
    {
        _chunked.ClearChunks();
        _chunked.enabled = false;

        _single.enabled = true;

        if (_meshRenderer != null)
            _meshRenderer.enabled = true;

        _single.RebuildMesh(model);
    }

    /// <summary>Enables and rebuilds the chunked renderer.</summary>
    private void RebuildChunked(VolumeModel model)
    {
        if (model.dataStructure != VolumeDataStructure.Octree)
        {
            Debug.LogWarning(
                "Chunked render mode currently supports only Octree data. " +
                "Falling back to SingleMesh for this rebuild.",
                this
            );

            RebuildSingle(model);
            return;
        }

        _single.Clear();
        _single.enabled = false;

        if (_meshRenderer != null)
            _meshRenderer.enabled = false;

        if (_meshFilter != null)
            _meshFilter.sharedMesh = null;

        _chunked.enabled = true;
        _chunked.RebuildChunks(model);
    }
}
