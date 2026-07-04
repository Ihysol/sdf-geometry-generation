using UnityEngine;

public interface IVolumeOperation
{
    BoundsInt AffectedRegion { get; }
    bool SupportsCpu { get; }
    bool SupportsGpu { get; }

    void ApplyCpu(IVolumeBuffer buffer);
    void ApplyGpu(IVolumeBuffer buffer, UnityEngine.Rendering.CommandBuffer commandBuffer);
}
