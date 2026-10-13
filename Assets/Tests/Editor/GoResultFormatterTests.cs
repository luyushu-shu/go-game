using NUnit.Framework;

public class GoResultFormatterTests
{
    [Test]
    public void BlackResignMeansWhiteMiddleGameWin()
    {
        Assert.AreEqual("白方中盘胜", GoResultFormatter.FormatResign(GoRules.BLACK));
    }

    [Test]
    public void ScoreUsesAbsoluteDifferenceWithOneDecimal()
    {
        Assert.AreEqual("黑方胜 2.5 目", GoResultFormatter.FormatScore(12.0f, 9.5f));
        Assert.AreEqual("白方胜 2.5 目", GoResultFormatter.FormatScore(9.5f, 12.0f));
    }

    [Test]
    public void TimeoutNamesTheWinnerAndLosingColor()
    {
        Assert.AreEqual("白方胜（黑方超时负）", GoResultFormatter.FormatTimeout(GoRules.BLACK));
        Assert.AreEqual("黑方胜（白方超时负）", GoResultFormatter.FormatTimeout(GoRules.WHITE));
    }

    [Test]
    public void FinishByTimeoutSetsCanonicalResultAndOverPhase()
    {
        var rules = new GoRules(9);
        rules.FinishByTimeout(GoRules.BLACK);

        Assert.AreEqual("白方胜（黑方超时负）", rules.Result);
        Assert.AreEqual(GoPhase.Over, rules.Phase);
    }

    [Test]
    public void ResignBySetsCanonicalResultAndOverPhase()
    {
        var rules = new GoRules(9);
        rules.ResignBy(GoRules.WHITE);

        Assert.AreEqual("黑方中盘胜", rules.Result);
        Assert.AreEqual(GoPhase.Over, rules.Phase);
    }

    [Test]
    public void ConfirmEndDoesNotOverwriteExistingResult()
    {
        var rules = new GoRules(9);
        rules.Pass();
        rules.Pass();
        var state = rules.ExportState();
        state.result = "既有终局结果";
        Assert.IsTrue(rules.ImportState(state));

        rules.ConfirmEnd();

        Assert.AreEqual("既有终局结果", rules.Result);
        Assert.AreEqual(GoPhase.Scoring, rules.Phase);
    }
}
