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

    public void Clear()
    {
        EnsureSetup();

        _single.Clear();
        _chunked.ClearChunks();

        if (_meshFilter != null)
            _meshFilter.sharedMesh = null;
    }

    private void RebuildSingle(VolumeModel model)
    {
        _chunked.ClearChunks();
        _chunked.enabled = false;

        _single.enabled = true;

        if (_meshRenderer != null)
            _meshRenderer.enabled = true;

        _single.RebuildMesh(model);
    }

    private void RebuildChunked(VolumeModel model)
    {
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