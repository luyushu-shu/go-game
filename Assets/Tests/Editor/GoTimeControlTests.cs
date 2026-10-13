using NUnit.Framework;

public class GoTimeControlTests
{
    [Test]
    public void FiveMinutesCreates300Seconds()
    {
        var value = GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None);
        Assert.AreEqual(5 * 60, value.MainTimeSeconds);
        Assert.AreEqual(0, value.ByoyomiPeriods);
    }
}
