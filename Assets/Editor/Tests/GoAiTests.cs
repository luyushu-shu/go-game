using System;
using UnityEngine;

public static class GoAiTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion Failed: " + message);
    }

    public static void RunAll()
    {
        TestMoveResultFactory();
        TestLocalMctsSearch();
        Debug.Log("[GoAiTests] All AI tests passed successfully!");
    }

    public static void TestMoveResultFactory()
    {
        var m = GoMoveResult.Move(3, 4, 0.65f, 2.5f);
        Assert(m.success, "Move result must be success");
        Assert(m.x == 3 && m.y == 4, "Move coordinates must match");
        Assert(Mathf.Approximately(m.winrate, 0.65f), "Winrate must match");
        Assert(Mathf.Approximately(m.scoreLead, 2.5f), "Score lead must match");
        Assert(!m.isPass && !m.isResign, "Should not be pass or resign");

        var p = GoMoveResult.Pass();
        Assert(p.success && p.isPass && !p.isResign, "Pass result must be pass");

        var r = GoMoveResult.Resign();
        Assert(r.success && !r.isPass && r.isResign, "Resign result must be resign");

        var f = GoMoveResult.Fail("Test error");
        Assert(!f.success && f.error == "Test error", "Fail result must have error");
    }

    public static void TestLocalMctsSearch()
    {
        var rules = new GoRules(9);
        rules.Play(4, 4, out _); // Black plays center

        GoMoveResult result = null;
        bool done = false;

        GoAI.SearchAsync(rules, GoRules.WHITE, 0.3f, res =>
        {
            result = res;
            done = true;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done && sw.ElapsedMilliseconds < 5000)
        {
            System.Threading.Thread.Sleep(20);
        }

        Assert(done, "Local MCTS search should complete within timeout");
        Assert(result != null, "Search result should not be null");
        Assert(result.success, "Search result should be success: " + result.error);
        Assert((result.x >= 0 && result.y >= 0 && result.x < 9 && result.y < 9) || result.isPass,
            $"Result move ({result.x},{result.y}) must be valid or pass");
    }
}
