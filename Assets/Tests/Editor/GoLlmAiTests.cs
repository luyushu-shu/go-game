using NUnit.Framework;

public class GoLlmAiTests
{
    [Test]
    public void ParsedResultsRemainRequestLocal()
    {
        var move = GoLlmAi.ParseMoveResult("D4");
        var pass = GoLlmAi.ParseMoveResult("pass");

        Assert.IsTrue(move.success);
        Assert.IsFalse(move.isPass);
        Assert.AreEqual(3, move.x);
        Assert.AreEqual(3, move.y);
        Assert.IsTrue(pass.success);
        Assert.IsTrue(pass.isPass);
        Assert.AreEqual(-1, pass.x);
        Assert.AreEqual(-1, pass.y);
        Assert.AreNotSame(move, pass);
        Assert.AreEqual(3, move.x);
        Assert.AreEqual(3, move.y);
    }

    [Test]
    public void InvalidParsedResultCarriesOnlyItsOwnError()
    {
        var invalid = GoLlmAi.ParseMoveResult("not a move");
        var move = GoLlmAi.ParseMoveResult("Q16");

        Assert.IsFalse(invalid.success);
        StringAssert.Contains("无法解析着法", invalid.error);
        Assert.IsTrue(move.success);
        Assert.AreEqual(15, move.x);
        Assert.AreEqual(15, move.y);
    }
}
