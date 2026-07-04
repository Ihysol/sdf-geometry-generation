using System;

public static class MesherFactory
{
    public static IVolumeMesher Create(PipelineMesherType type)
    {
        switch (type)
        {
            case PipelineMesherType.Voxel:
                return new VoxelMesher();
            case PipelineMesherType.MarchingCubes:
                return new MarchingCubesMesher();
            case PipelineMesherType.SurfaceNets:
                return new SurfaceNetsMesher();
            default:
                throw new ArgumentException($"Unknown mesher type: {type}");
        }
    }
}
