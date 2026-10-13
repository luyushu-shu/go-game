using NUnit.Framework;

public class GoKifuSgfTests
{
    [Test]
    public void BuildSgfPreservesBoardSizeKomiPassAndMoves()
    {
        var record = new KifuRecord
        {
            boardSize = 9,
            komi = 7.5f,
            blackName = "黑方",
            whiteName = "白方",
            moveLogCsv = "2,2,1|-1,-1,2|3,3,1"
        };

        var sgf = KifuDatabase.BuildSgf(record);

        StringAssert.Contains("CA[UTF-8]SZ[9]KM[7.5]", sgf);
        StringAssert.Contains(";B[cc]", sgf);
        StringAssert.Contains(";W[]", sgf);
        StringAssert.Contains(";B[dd]", sgf);
    }

    [Test]
    public void BuildSgfKeepsScoreAndTimeoutResults()
    {
        var score = new KifuRecord { boardSize = 19, result = "白方胜 2.5 目" };
        var timeout = new KifuRecord { boardSize = 19, result = "白方胜（黑方超时负）" };

        StringAssert.Contains("RE[白方胜 2.5 目]", KifuDatabase.BuildSgf(score));
        StringAssert.Contains("RE[白方胜（黑方超时负）]", KifuDatabase.BuildSgf(timeout));
    }

    [Test]
    public void BuildSgfEscapesSpecialCharactersAndIsDeterministic()
    {
        var record = new KifuRecord
        {
            boardSize = 19,
            blackName = "黑]方\\棋手",
            description = "备注]\\内容",
            result = "黑方中盘胜]\\"
        };

        var first = KifuDatabase.BuildSgf(record);
        var second = KifuDatabase.BuildSgf(record);

        Assert.AreEqual(first, second);
        StringAssert.Contains("PB[黑\\]方\\\\棋手]", first);
        StringAssert.Contains("C[备注\\]\\\\内容]", first);
        StringAssert.Contains("RE[黑方中盘胜\\]\\\\]", first);
    }

    [Test]
    public void RestoredHandicapHasSavePriority()
    {
        Assert.AreEqual(4, GoGame.ResolveKifuSaveHandicap(true, 4, false, 0));
    }

    [Test]
    public void NewStudySaveDoesNotInheritRestoredHandicap()
    {
        Assert.AreEqual(0, GoGame.ResolveKifuSaveHandicap(false, 4, true, 9));
        Assert.AreEqual(3, GoGame.ResolveKifuSaveHandicap(false, 4, false, 3));
    }

    [Test]
    public void NewBranchStepStartsWithoutAnalysis()
    {
        var step = GoGame.CreateUnanalyzedBranchStep(1, 2, 3, GoRules.BLACK, false);

        Assert.IsFalse(step.hasAnalysis);
        Assert.AreEqual(-1f, step.winrate);
        Assert.AreEqual(0f, step.scoreLead);
    }
}
