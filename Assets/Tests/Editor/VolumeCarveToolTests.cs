using NUnit.Framework;
using UnityEditor;

public class VolumeCarveToolTests
{
    private bool _wasEnabled;
    private Tool _previousTool;

    [SetUp]
    public void SetUp()
    {
        _wasEnabled = VolumeCarveTool.IsEnabled;
        _previousTool = Tools.current;
        VolumeCarveTool.SetEnabled(false);
    }

    [TearDown]
    public void TearDown()
    {
        VolumeCarveTool.SetEnabled(false);
        Tools.current = _previousTool;
        if (_wasEnabled)
            VolumeCarveTool.SetEnabled(true);
    }

    [Test]
    public void SetEnabled_ActivatingCarveReleasesUnityTransformTool()
    {
        Tools.current = Tool.Move;

        VolumeCarveTool.SetEnabled(true);

        Assert.That(VolumeCarveTool.IsEnabled, Is.True);
        Assert.That(Tools.current, Is.EqualTo(Tool.None));
    }

    [Test]
    public void SynchronizeWithUnityTool_SelectingMoveDisablesCarve()
    {
        VolumeCarveTool.SetEnabled(true);
        Tools.current = Tool.Move;

        VolumeCarveTool.SynchronizeWithUnityTool();

        Assert.That(VolumeCarveTool.IsEnabled, Is.False);
        Assert.That(Tools.current, Is.EqualTo(Tool.Move));
    }

    [Test]
    public void SetEnabled_DisablingCarveCancelsActiveInteraction()
    {
        VolumeCarveTool.SetEnabled(true);

        VolumeCarveTool.SetEnabled(false);

        Assert.That(VolumeCarveTool.IsEnabled, Is.False);
        Assert.That(VolumeCarveTool.IsCarving, Is.False);
    }
}
