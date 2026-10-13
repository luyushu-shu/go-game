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

    [Test]
    public void CustomRangeAcceptsOneMinuteAnd600Minutes()
    {
        var low = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.None);
        low.MainTimeSeconds = 60;
        Assert.IsTrue(low.TryValidate(out _));
        low.MainTimeSeconds = 600 * 60;
        Assert.IsTrue(low.TryValidate(out _));
    }

    [Test]
    public void CustomRangeRejectsZeroAnd601Minutes()
    {
        var value = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.None);
        value.MainTimeSeconds = 0;
        Assert.IsFalse(value.TryValidate(out _));
        value.MainTimeSeconds = 601 * 60;
        Assert.IsFalse(value.TryValidate(out _));
    }

    [Test]
    public void CustomByoyomiAcceptsOneTo20PeriodsAndFiveTo600Seconds()
    {
        var value = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.Custom);
        value.MainTimeSeconds = 60;
        value.ByoyomiPeriods = 1;
        value.ByoyomiSeconds = 5;
        Assert.IsTrue(value.TryValidate(out _));
        value.ByoyomiPeriods = 20;
        value.ByoyomiSeconds = 600;
        Assert.IsTrue(value.TryValidate(out _));
    }

    [Test]
    public void CustomByoyomiRejectsZeroMainTime()
    {
        var value = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.Custom);
        value.MainTimeSeconds = 0;
        value.ByoyomiPeriods = 1;
        value.ByoyomiSeconds = 5;
        Assert.IsFalse(value.TryValidate(out _));
    }
}
