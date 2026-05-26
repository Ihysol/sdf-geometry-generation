using UnityEngine;

public interface IFlatDenseVolumeData : IVolumeData
{
    Vector3Int GridSize { get; }
    Vector3 Origin { get; }
    Vector3 CellSize { get; }
    float[] Values { get; }
}
