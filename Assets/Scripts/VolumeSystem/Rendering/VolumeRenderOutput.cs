using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(VolumeMeshRenderer))]
public class VolumeRenderOutput : MonoBehaviour
{
    private VolumeMeshRenderer _renderer;

    public VolumeMeshRenderer.RenderStats LastRenderStats
    {
        get
        {
            EnsureSetup();
            return _renderer != null ? _renderer.LastRenderStats : default;
        }
    }

    /// <summary>Fetches the renderer component.</summary>
    private void EnsureSetup()
    {
        if (_renderer == null)
            _renderer = GetComponent<VolumeMeshRenderer>();
    }

    /// <summary>Rebuilds output via the unified volume renderer.</summary>
    public void Rebuild(VolumeModel model)
    {
        EnsureSetup();
        _renderer?.Rebuild(model);
    }

    public void DrainPendingChunksImmediately()
    {
        EnsureSetup();
        _renderer?.DrainPendingChunksImmediately();
    }

    public bool CanBuildDirtyChunksLocally(VolumeModel model)
    {
        EnsureSetup();
        return _renderer != null && _renderer.CanBuildDirtyChunksLocally(model);
    }

    /// <summary>Clears generated output.</summary>
    public void Clear()
    {
        EnsureSetup();
        _renderer?.Clear();
    }
}
