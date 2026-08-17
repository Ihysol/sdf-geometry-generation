using NUnit.Framework;
using UnityEditor;

public class VolumeCarveToolTests
{
    private bool _wasEnabled;

    [SetUp]
    public void SetUp()
    {
        _wasEnabled = VolumeCarveTool.IsEnabled;
        VolumeCarveTool.SetEnabled(false);
    }

    [TearDown]
    public void TearDown()
    {
        if (_wasEnabled)
            VolumeCarveTool.SetEnabled(true);
    }

    [Test]
    public void SetEnabled_ToggleWorksWithoutChangingUnityTool()
    {
        Tools.current = Tool.Move;

        VolumeCarveTool.SetEnabled(true);

        Assert.That(VolumeCarveTool.IsEnabled, Is.True);
        // New approach: carve tool co-exists with Unity's transform tool
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
