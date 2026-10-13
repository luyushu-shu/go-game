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
}
