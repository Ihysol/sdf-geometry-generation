using System;

public static class MesherFactory
{
    public static IVolumeMesher Create(PipelineMesherType type)
    {
        switch (type)
        {
            case PipelineMesherType.Voxel:
                return new VoxelMesher();
            case PipelineMesherType.GreedyVoxel:
                return new GreedyVoxelMesher();
            case PipelineMesherType.MarchingCubes:
                return new MarchingCubesMesher();
            case PipelineMesherType.SurfaceNets:
                return new SurfaceNetsMesher();
            case PipelineMesherType.DualContouring:
                return CreateDualContouring();
            case PipelineMesherType.GpuVoxel:
                return new GpuVoxelMesher();
            default:
                throw new ArgumentException($"Unknown mesher type: {type}");
        }
    }

    private static DualContouringMesher CreateDualContouring(int edgeRefinementSteps = 8)
    {
        var settings = DualContouringSettings.Default();
        settings.UseQefVertices = true;
        settings.EdgeRefinementSteps = edgeRefinementSteps;
        settings.QefSolverSettings.irlsIterations = 6;
        settings.QefSolverSettings.robustKernel = QefSolver.RobustKernel.Cauchy;
        settings.QefSolverSettings.robustScale = 2.5f;

        var dc = new DualContouringMesher();
        dc.Settings = settings;
        return dc;
    }
}
