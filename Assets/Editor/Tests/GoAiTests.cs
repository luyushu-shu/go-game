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
        TestKataGoResponseReaderTimeoutAndFraming();
        TestLocalMctsSearch();
        TestLocalMctsFailureCompletes();
        TestCompletionLatchPublishesResultBeforeDone();
        TestLocalMctsUsesCallerThreadSnapshot();
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

    public static void TestKataGoResponseReaderTimeoutAndFraming()
    {
        using (var reader = new GoKataGoResponseReader())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool timedOut = reader.TryReadUntilBlank(40, out _);
            Assert(!timedOut, "An incomplete KataGo response should time out");
            Assert(sw.ElapsedMilliseconds < 1000, "Response timeout must not block on ReadLine");

            reader.AddLine("= protocol_version 2");
            reader.AddLine("");
            Assert(reader.TryReadUntilBlank(100, out string response), "A blank-terminated response should complete");
            Assert(response == "= protocol_version 2", "Response framing should preserve the response line");
        }
    }

    public static void TestLocalMctsFailureCompletes()
    {
        GoMoveResult result = null;
        bool done = false;
        GoAI.SearchAsync(null, GoRules.BLACK, 0.1f, res =>
        {
            result = res;
            done = true;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done && sw.ElapsedMilliseconds < 5000)
            System.Threading.Thread.Sleep(20);

        Assert(done, "Invalid local search must still complete its callback");
        Assert(result != null && !result.success, "Invalid local search must return a failure result");
        Assert(!string.IsNullOrEmpty(result.error), "Failure result should explain the search error");
    }

    public static void TestCompletionLatchPublishesResultBeforeDone()
    {
        for (int i = 0; i < 100; i++)
        {
            var latch = new GoAiCompletionLatch();
            var expected = GoMoveResult.Move(i % 9, (i / 9) % 9);
            System.Threading.Tasks.Task.Run(() => latch.TryPublish(expected));

            GoMoveResult actual = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!latch.TryGetResult(out actual) && sw.ElapsedMilliseconds < 2000)
                System.Threading.Thread.Sleep(1);

            Assert(actual != null, "Published completion must expose a result");
            Assert(object.ReferenceEquals(expected, actual), "Completion must publish the expected result before done");
            Assert(!latch.TryPublish(GoMoveResult.Fail("duplicate")), "Completion must publish exactly once");
        }
    }

    public static void TestLocalMctsUsesCallerThreadSnapshot()
    {
        var rules = new GoRules(9);
        for (int i = 0; i < rules.Board.Length; i++)
            rules.Board[i] = GoRules.BLACK;

        GoMoveResult result = null;
        bool done = false;
        using (var releaseWorker = new System.Threading.ManualResetEventSlim(false))
        {
            GoAI.BeforeWorkerSearchForTests = () => releaseWorker.Wait(2000);
            try
            {
                GoAI.SearchAsync(rules, GoRules.WHITE, 0.01f, res =>
                {
                    result = res;
                    done = true;
                });

                // If the worker still reads GoRules, this reset changes the search
                // to an empty 19x19 board. A caller-thread snapshot stays full 9x9.
                rules.Reset(19);
                releaseWorker.Set();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!done && sw.ElapsedMilliseconds < 5000)
                    System.Threading.Thread.Sleep(10);
            }
            finally
            {
                releaseWorker.Set();
                GoAI.BeforeWorkerSearchForTests = null;
            }
        }

        Assert(done, "Snapshot-isolation search should complete");
        Assert(result != null && result.success, "Snapshot-isolation search should return a result");
        Assert(result.isPass, "The full 9x9 snapshot should pass even after the live rules reset to 19x19");
    }
}
