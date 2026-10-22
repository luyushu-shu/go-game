using System;
using System.Diagnostics;
using NUnit.Framework;

public class GoSituationEstimatorTests
{
    [Test]
    public void ConfirmedScoreUsesTheReviewedSituationRatherThanRecounting()
    {
        var rules = new GoRules(9); rules.Play(2, 2, out _); rules.Play(6, 6, out _);
        Assert.IsTrue(rules.TryGetSituation(out var reviewed));
        string expected = GoResultFormatter.FormatScore(reviewed.BlackScore, reviewed.WhiteScore);
        Assert.IsTrue(rules.ConfirmSituationResult(reviewed));
        Assert.AreEqual(expected, rules.Result);
        Assert.AreEqual(GoPhase.Over, rules.Phase);
        Assert.IsFalse(rules.ConfirmSituationResult(reviewed));
    }

    [Test]
    public void StaleReviewedScoreCannotEndAnUpdatedGame()
    {
        var rules = new GoRules(9); rules.TryGetSituation(out var reviewed);
        rules.Play(2, 2, out _);
        Assert.IsFalse(rules.ConfirmSituationResult(reviewed));
        Assert.IsNull(rules.Result); Assert.AreEqual(GoPhase.Play, rules.Phase);
    }

    [Test]
    public void ChangedKomiCannotConfirmAnOldScore()
    {
        var rules = new GoRules(9); rules.TryGetSituation(out var reviewed);
        rules.Komi = 6.5f;
        Assert.IsFalse(rules.ConfirmSituationResult(reviewed));
        Assert.IsNull(rules.Result);
    }

    [Test]
    public void CornerStoneWithNoLegalEscapeIsMarkedAsOneDeadChain()
    {
        var board = new int[81]; board[0] = 1;
        foreach (int point in new[] { 9, 10, 11, 2 }) board[point] = 2;
        var result = GoSituationEstimator.Estimate(board, 9, 7.5f, 1);
        Assert.AreEqual(1, result.DeadBlack);
        Assert.AreEqual(2, result.Owner[0]);
        Assert.AreEqual(0, result.BlackStones);
    }

    [UnityEngine.TestTools.UnityTest]
    public System.Collections.IEnumerator BackgroundEstimateDiscardsAChangedPosition()
    {
        var rules = new GoRules(9);
        rules.TryGetSituation(out _, true);
        rules.Play(2, 2, out _);
        GoSituationEstimator.Result result = null;
        float deadline = UnityEngine.Time.realtimeSinceStartup + 10;
        while (!rules.TryGetSituation(out result, true) && UnityEngine.Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.NotNull(result);
        Assert.AreEqual(1, result.BlackStones, "The earlier empty-board result must not be published.");
        Assert.AreEqual(1, result.Owner[20]);
    }

    [Test]
    public void EmptyBoardIsNeutralAndOnlyKomiSeparatesScores()
    {
        var result = GoSituationEstimator.Estimate(new int[81], 9, 7.5f, 1);
        CollectionAssert.AreEqual(new int[81], result.Owner);
        Assert.AreEqual(0, result.BlackScore); Assert.AreEqual(7.5f, result.WhiteScore);
    }

    [TestCase(9)]
    [TestCase(13)]
    [TestCase(19)]
    public void MirroredStonesProduceMirroredOwnersWithoutMutatingPosition(int size)
    {
        var board = new int[size * size]; int a = size > 9 ? 3 : 2;
        board[a * size + a] = 1; board[a * size + size - 1 - a] = 2;
        var before = (int[])board.Clone();
        var watch = Stopwatch.StartNew();
        var result = GoSituationEstimator.Estimate(board, size, 7.5f, 1);
        UnityEngine.Debug.Log("Estimator " + size + "x" + size + ": " + watch.ElapsedMilliseconds + " ms");
        CollectionAssert.AreEqual(before, board);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int owner = result.Owner[y * size + x], mirrored = result.Owner[y * size + size - 1 - x];
            Assert.AreEqual(owner == 0 ? 0 : 3 - owner, mirrored);
        }
        Assert.AreEqual(result.BlackTerritory, result.WhiteTerritory);
        Assert.Greater(result.BlackTerritory, 0, "Stable corner roots should produce local ownership evidence.");
        Assert.AreEqual(0, result.Owner[(size - 1) * size + size / 2], "Distant open board must remain neutral.");
        UnityEngine.Debug.Log("Estimated empty points: " + result.BlackTerritory + "/" + result.WhiteTerritory);
    }

    static int[] TwoEyes()
    {
        var board = new int[81];
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 5; x++) board[y * 9 + x] = 1;
        board[2 * 9 + 2] = board[2 * 9 + 4] = 0;
        return board;
    }

    [Test]
    public void BensonProvesTwoEyesButDoesNotDeclareOneEyeDead()
    {
        var board = TwoEyes(); var safe = GoSituationEstimator.FindSafeStones(board, 9);
        Assert.IsTrue(safe[10]);
        board[2 * 9 + 4] = 1;
        safe = GoSituationEstimator.FindSafeStones(board, 9);
        Assert.IsFalse(safe[10]);
        var result = GoSituationEstimator.Estimate(board, 9, 7.5f, 1);
        Assert.AreEqual(0, result.DeadBlack, "No proof of life is not proof of death.");
    }

    [Test]
    public void ProvenEyesSurviveRolloutsAndCountsMatchEveryOwner()
    {
        var result = GoSituationEstimator.Estimate(TwoEyes(), 9, 6.5f, 1);
        Assert.AreEqual(1, result.Owner[20]); Assert.AreEqual(1, result.Owner[22]);
        int black = 0, white = 0;
        foreach (int owner in result.Owner) { if (owner == 1) black++; else if (owner == 2) white++; }
        Assert.AreEqual(black, result.BlackScore);
        Assert.AreEqual(white + 6.5f, result.WhiteScore);
        Assert.AreEqual(0, result.DeadBlack);
    }

    [Test]
    public void CacheUpdatesForBoardKomiAndUndo()
    {
        var rules = new GoRules(9);
        rules.TryGetSituation(out var first);
        rules.Komi = 6.5f; rules.TryGetSituation(out var komi);
        Assert.AreEqual(first.WhiteScore - 1, komi.WhiteScore);
        rules.Play(2, 2, out _); rules.TryGetSituation(out var moved);
        Assert.AreNotSame(komi, moved);
        rules.Undo(1); rules.TryGetSituation(out var undone);
        CollectionAssert.AreEqual(komi.Owner, undone.Owner);
    }

    [Test]
    public void RepeatedEstimateHasExactlyTheSameResult()
    {
        var board = new int[81]; board[20] = 1; board[60] = 2;
        var a = GoSituationEstimator.Estimate(board, 9, 7.5f, 1);
        var b = GoSituationEstimator.Estimate(board, 9, 7.5f, 1);
        CollectionAssert.AreEqual(a.Owner, b.Owner);
        CollectionAssert.AreEqual(a.Confidence, b.Confidence);
    }
}
