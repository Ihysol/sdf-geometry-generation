using UnityEngine;

public struct MeshingContext
{
    public float IsoLevel;
    public float CellSize;
    public BoundsInt Region;
    public bool GenerateNormals;
    public bool GenerateMaterials;

    public static MeshingContext Default(VolumeLayout layout)
    {
        return new MeshingContext
        {
            IsoLevel = layout.IsoLevel,
            CellSize = layout.CellSize,
            Region = new BoundsInt(0, 0, 0, layout.Resolution.x, layout.Resolution.y, layout.Resolution.z),
            GenerateNormals = true,
            GenerateMaterials = false
        };
    }
}
