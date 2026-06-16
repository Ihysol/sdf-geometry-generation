using NUnit.Framework;
using UnityEngine;

public class VolumeModelRebuildModeTests
{
    private GameObject _gameObject;
    private VolumeModel _model;

    [SetUp]
    public void SetUp()
    {
        _gameObject = new GameObject("VolumeModelRebuildModeTests");
        _model = _gameObject.AddComponent<VolumeModel>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
    }

    [Test]
    public void PreviewAndOnChange_IsTheDefaultMode()
    {
        Assert.That(_model.rebuildMode, Is.EqualTo(VolumeRebuildMode.PreviewAndOnChange));
        Assert.That(_model.ShouldAutoRebuildOnChange(), Is.True);
        Assert.That(_model.ShouldAutoRebuildOnTransformChange(), Is.True);
        Assert.That(_model.ShouldUseInteractionPreview(), Is.True);
        Assert.That(_model.ShouldRebuildEveryFrame(), Is.False);
    }

    [Test]
    public void OnChange_RebuildsChangesWithoutPreview()
    {
        _model.rebuildMode = VolumeRebuildMode.OnChange;

        Assert.That(_model.ShouldAutoRebuildOnChange(), Is.True);
        Assert.That(_model.ShouldAutoRebuildOnTransformChange(), Is.True);
        Assert.That(_model.ShouldUseInteractionPreview(), Is.False);
        Assert.That(_model.ShouldRebuildEveryFrame(), Is.False);
    }

    [Test]
    public void EveryFrame_OnlyRebuildsFromUpdate()
    {
        _model.rebuildMode = VolumeRebuildMode.EveryFrame;

        Assert.That(_model.ShouldAutoRebuildOnChange(), Is.False);
        Assert.That(_model.ShouldAutoRebuildOnTransformChange(), Is.False);
        Assert.That(_model.ShouldUseInteractionPreview(), Is.False);
        Assert.That(_model.ShouldRebuildEveryFrame(), Is.True);
    }

    [Test]
    public void VolumeModel_RunsUpdatesInEditMode()
    {
        Assert.That(
            System.Attribute.IsDefined(typeof(VolumeModel), typeof(ExecuteAlways)),
            Is.True
        );
    }

    [Test]
    public void Manual_DisablesImplicitRebuilds()
    {
        _model.rebuildMode = VolumeRebuildMode.Manual;

        Assert.That(_model.ShouldAutoRebuildOnChange(), Is.False);
        Assert.That(_model.ShouldAutoRebuildOnTransformChange(), Is.False);
        Assert.That(_model.ShouldUseInteractionPreview(), Is.False);
        Assert.That(_model.ShouldRebuildEveryFrame(), Is.False);
    }

    [Test]
    public void PreviewQefSimplification_DisablesQefForPreviewOnly()
    {
        _model.useQefVertices = true;
        _model.qefVertexMode = QefVertexMode.QefAxisSnap;
        _model.qefEnableMultiHermite = true;
        _model.simplifyQefDuringPreview = true;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveUseQefVertices(), Is.False);
            Assert.That(_model.GetEffectiveQefVertexMode(), Is.EqualTo(QefVertexMode.AverageCrossings));
            Assert.That(_model.GetEffectiveQefEnableMultiHermite(), Is.False);
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }

        Assert.That(_model.GetEffectiveUseQefVertices(), Is.True);
        Assert.That(_model.GetEffectiveQefVertexMode(), Is.EqualTo(QefVertexMode.QefAxisSnap));
        Assert.That(_model.GetEffectiveQefEnableMultiHermite(), Is.True);
    }

    [Test]
    public void PreviewQefSimplification_CanBeDisabled()
    {
        _model.useQefVertices = true;
        _model.qefVertexMode = QefVertexMode.QefAxisSnap;
        _model.qefEnableMultiHermite = true;
        _model.simplifyQefDuringPreview = false;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveUseQefVertices(), Is.True);
            Assert.That(_model.GetEffectiveQefVertexMode(), Is.EqualTo(QefVertexMode.QefAxisSnap));
            Assert.That(_model.GetEffectiveQefEnableMultiHermite(), Is.True);
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }
    }

    [Test]
    public void FlatPreviewMeshing_UsesFlatStorageForOctreeDualContouringPreviewOnly()
    {
        _model.dataStructure = VolumeDataStructure.Octree;
        _model.octreeMesherType = OctreeMesherType.DualContouring;
        _model.storageMode = VolumeStorageMode.Tree;
        _model.useFlatDualContouringPreview = true;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveStorageMode(), Is.EqualTo(VolumeStorageMode.Flat));
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }

        Assert.That(_model.GetEffectiveStorageMode(), Is.EqualTo(VolumeStorageMode.Tree));
    }

    [Test]
    public void FlatPreviewMeshing_DoesNotOverrideNonDualContouringMesher()
    {
        _model.dataStructure = VolumeDataStructure.Octree;
        _model.octreeMesherType = OctreeMesherType.SurfaceNets;
        _model.storageMode = VolumeStorageMode.Tree;
        _model.useFlatDualContouringPreview = true;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveStorageMode(), Is.EqualTo(VolumeStorageMode.Tree));
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }
    }

    [Test]
    public void PreviewEdgeRefinement_UsesPreviewValueOnlyDuringPreview()
    {
        _model.edgeRefinementSteps = 3;
        _model.previewEdgeRefinementSteps = 2;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveEdgeRefinementSteps(), Is.EqualTo(2));
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }

        Assert.That(_model.GetEffectiveEdgeRefinementSteps(), Is.EqualTo(3));
    }

    [Test]
    public void PreviewEdgeRefinement_NeverExceedsFinalValue()
    {
        _model.edgeRefinementSteps = 2;
        _model.previewEdgeRefinementSteps = 5;

        bool previous = _model.SetPreviewRebuildContext(true);

        try
        {
            Assert.That(_model.GetEffectiveEdgeRefinementSteps(), Is.EqualTo(2));
        }
        finally
        {
            _model.RestorePreviewRebuildContext(previous);
        }
    }

    [Test]
    public void VolumeObject_EstimatesDirtyBoundsForLocalMove()
    {
        VolumeObject volumeObject = _gameObject.AddComponent<VolumeObject>();
        volumeObject.shapeType = VolumeShapeType.Sphere;
        volumeObject.sphereRadius = 1f;

        Bounds dirtyBounds = volumeObject.EstimateLocalMoveDirtyBounds(
            Vector3.zero,
            new Vector3(2f, 0f, 0f)
        );

        Assert.That(dirtyBounds.Contains(new Vector3(-1f, 0f, 0f)), Is.True);
        Assert.That(dirtyBounds.Contains(new Vector3(3f, 0f, 0f)), Is.True);
    }

}
