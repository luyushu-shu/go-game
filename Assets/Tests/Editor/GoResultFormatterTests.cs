using NUnit.Framework;

public class GoResultFormatterTests
{
    [Test]
    public void BlackResignMeansWhiteMiddleGameWin()
    {
        Assert.AreEqual("白方中盘胜", GoResultFormatter.FormatResign(GoRules.BLACK));
    }
}
