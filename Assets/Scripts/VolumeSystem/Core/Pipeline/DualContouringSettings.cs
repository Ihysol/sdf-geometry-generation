using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public struct DualContouringSettings
{
    public bool UseQefVertices;
    public int EdgeRefinementSteps;
    public QefSolver.Settings QefSolverSettings;

    public static DualContouringSettings Default()
    {
        return new DualContouringSettings
        {
            UseQefVertices = true,
            EdgeRefinementSteps = 8,
            QefSolverSettings = new QefSolver.Settings
            {
                irlsIterations = 6,
                robustKernel = QefSolver.RobustKernel.Cauchy,
                robustScale = 2.5f,
                useAnisotropicRegularization = false,
                anisotropicStrength = 0.2f
            }
        };
    }
}
