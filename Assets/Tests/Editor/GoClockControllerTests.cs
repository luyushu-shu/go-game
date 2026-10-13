using NUnit.Framework;

public class GoClockControllerTests
{
    [Test]
    public void MainTimeConsumesOneSecond()
    {
        var clock = new GoClockController();
        clock.Start(GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None), GoRules.BLACK);
        clock.Tick(1.0);
        Assert.AreEqual(299.0, clock.GetSnapshot(GoRules.BLACK).MainSeconds, 0.001);
    }

    [Test]
    public void MainTimeExpiryEntersThirtySecondByoyomi()
    {
        var control = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.FiveByThirty);
        control.MainTimeSeconds = 1;
        var clock = new GoClockController();
        clock.Start(control, GoRules.BLACK);
        clock.Tick(1.0);
        var snapshot = clock.GetSnapshot(GoRules.BLACK);
        Assert.IsTrue(snapshot.InByoyomi);
        Assert.AreEqual(30.0, snapshot.ByoyomiSeconds, 0.001);
    }

    [Test]
    public void FiveExpiredByoyomiPeriodsTriggerOneTimeout()
    {
        var control = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.FiveByThirty);
        control.MainTimeSeconds = 1;
        var clock = new GoClockController();
        int timeoutCount = 0;
        clock.TimedOut += _ => timeoutCount++;
        clock.Start(control, GoRules.BLACK);
        clock.Tick(1.0 + 30.0 * 5.0);
        Assert.AreEqual(1, timeoutCount);
    }

    [Test]
    public void PauseStopsConsumption()
    {
        var clock = new GoClockController();
        clock.Start(GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None), GoRules.BLACK);
        clock.Pause();
        clock.Tick(10.0);
        Assert.AreEqual(300.0, clock.GetSnapshot(GoRules.BLACK).MainSeconds, 0.001);
    }

    [Test]
    public void OnlyActiveColorConsumesTime()
    {
        var clock = new GoClockController();
        clock.Start(GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None), GoRules.BLACK);
        clock.Tick(1.0);

        Assert.AreEqual(299.0, clock.GetSnapshot(GoRules.BLACK).MainSeconds, 0.001);
        Assert.AreEqual(300.0, clock.GetSnapshot(GoRules.WHITE).MainSeconds, 0.001);
    }

    [Test]
    public void ResumeRestartsConsumption()
    {
        var clock = new GoClockController();
        clock.Start(GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None), GoRules.BLACK);
        clock.Pause();
        clock.Tick(1.0);
        clock.Resume();
        clock.Tick(1.0);

        Assert.AreEqual(299.0, clock.GetSnapshot(GoRules.BLACK).MainSeconds, 0.001);
    }

    [Test]
    public void StopPreventsConsumption()
    {
        var clock = new GoClockController();
        clock.Start(GoTimeControl.FromPreset(GoTimePreset.FiveMinutes, GoByoyomiPreset.None), GoRules.BLACK);
        clock.Stop();
        clock.Tick(1.0);

        Assert.AreEqual(300.0, clock.GetSnapshot(GoRules.BLACK).MainSeconds, 0.001);
    }

    [Test]
    public void TimeoutEventReportsLosingColor()
    {
        var control = GoTimeControl.FromPreset(GoTimePreset.Custom, GoByoyomiPreset.None);
        control.MainTimeSeconds = 1;
        var clock = new GoClockController();
        int losingColor = GoRules.EMPTY;
        clock.TimedOut += color => losingColor = color;
        clock.Start(control, GoRules.WHITE);
        clock.Tick(1.0);

        Assert.AreEqual(GoRules.WHITE, losingColor);
    }
}
