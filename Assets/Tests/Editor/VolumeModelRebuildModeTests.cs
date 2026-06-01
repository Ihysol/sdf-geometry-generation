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
}
