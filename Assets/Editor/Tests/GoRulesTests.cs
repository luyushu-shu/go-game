using System;
using System.Collections.Generic;
using UnityEngine;

public static class GoRulesTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion Failed: " + message);
    }

    public static void RunAll()
    {
        TestNormalMoveAndOccupied();
        TestSingleCapture();
        TestGroupCapture();
        TestSuicideForbidden();
        TestKoRule();
        TestConsecutivePassesAndScoring();
        TestUndoBoardAndHash();
        TestHandicapInitializationAndUndoWithKo();
        TestScoringAndKomi();
        TestStateDataExportImport();
        TestReplayToPly();
        Debug.Log("[GoRulesTests] All 11 rule tests passed successfully!");
    }

    public static void TestNormalMoveAndOccupied()
    {
        var rules = new GoRules(9);
        Assert(rules.Turn == GoRules.BLACK, "Initial turn must be BLACK");
        Assert(rules.MoveCount == 0, "Initial move count must be 0");

        bool ok = rules.Play(4, 4, out string msg);
        Assert(ok, "Black playing (4,4) should succeed: " + msg);
        Assert(rules.Board[rules.Idx(4, 4)] == GoRules.BLACK, "Point (4,4) must be BLACK");
        Assert(rules.Turn == GoRules.WHITE, "Turn should change to WHITE");
        Assert(rules.MoveCount == 1, "MoveCount should be 1");

        // White tries to play on occupied point (4,4)
        bool occupiedOk = rules.Play(4, 4, out msg);
        Assert(!occupiedOk, "Playing on occupied point should fail");

        // Out of bounds
        Assert(!rules.Play(-1, 0, out _), "Negative coordinate should fail");
        Assert(!rules.Play(9, 4, out _), "Out of range coordinate should fail");
    }

    public static void TestSingleCapture()
    {
        var rules = new GoRules(9);
        // Place black at corner (0,0)
        rules.Play(0, 0, out _); // B at (0,0)
        rules.Play(1, 0, out _); // W at (1,0)
        rules.Play(4, 4, out _); // B elsewhere
        rules.Play(0, 1, out _); // W at (0,1) captures B at (0,0)

        Assert(rules.Board[rules.Idx(0, 0)] == GoRules.EMPTY, "Black stone at (0,0) should be captured");
        Assert(rules.Captures[GoRules.WHITE] == 1, "White should have captured 1 stone");
    }

    public static void TestGroupCapture()
    {
        var rules = new GoRules(9);
        // Black connects two stones at (1,1) and (1,2)
        rules.Play(1, 1, out _); // B
        rules.Play(0, 1, out _); // W
        rules.Play(1, 2, out _); // B
        rules.Play(0, 2, out _); // W
        rules.Play(5, 5, out _); // B
        rules.Play(2, 1, out _); // W
        rules.Play(5, 6, out _); // B
        rules.Play(2, 2, out _); // W
        rules.Play(6, 6, out _); // B
        rules.Play(1, 0, out _); // W
        rules.Play(7, 7, out _); // B
        rules.Play(1, 3, out _); // W captures both black stones

        Assert(rules.Board[rules.Idx(1, 1)] == GoRules.EMPTY, "(1,1) must be empty after group capture");
        Assert(rules.Board[rules.Idx(1, 2)] == GoRules.EMPTY, "(1,2) must be empty after group capture");
        Assert(rules.Captures[GoRules.WHITE] == 2, "White should have captured 2 stones");
    }

    public static void TestSuicideForbidden()
    {
        var rules = new GoRules(9);
        // White surrounds (0,0)
        rules.Play(5, 5, out _); // B
        rules.Play(0, 1, out _); // W
        rules.Play(6, 6, out _); // B
        rules.Play(1, 0, out _); // W

        // Black cannot suicide at (0,0)
        bool suicideOk = rules.Play(0, 0, out string msg);
        Assert(!suicideOk, "Suicide move at (0,0) must be forbidden");
    }

    public static void TestKoRule()
    {
        var rules = new GoRules(9);
        // Build classic Ko:
        // B: (1,0), (0,1), (1,2)
        // W: (2,0), (3,1), (2,2)
        rules.Play(1, 0, out _); // B
        rules.Play(2, 0, out _); // W
        rules.Play(0, 1, out _); // B
        rules.Play(3, 1, out _); // W
        rules.Play(1, 2, out _); // B
        rules.Play(2, 2, out _); // W

        // Black passes to give turn to White
        rules.Pass(); // B pass -> Turn is WHITE

        // White plays at (1,1) - only liberty is (2,1)
        bool wPlay = rules.Play(1, 1, out string wMsg);
        Assert(wPlay, "White playing at (1,1) should succeed: " + wMsg);
        Assert(rules.Board[rules.Idx(1, 1)] == GoRules.WHITE, "(1,1) should be WHITE");

        // Black takes ko at (2,1), capturing W at (1,1)
        bool bTake = rules.Play(2, 1, out string bMsg);
        Assert(bTake, "Black taking ko should succeed: " + bMsg);
        Assert(rules.Board[rules.Idx(1, 1)] == GoRules.EMPTY, "W at (1,1) should be captured");

        // White immediately retaking at (1,1) is forbidden by Ko rule
        bool wRetake = rules.Play(1, 1, out string koMsg);
        Assert(!wRetake, "Immediate retake of Ko by White must be forbidden: " + koMsg);
        Assert(koMsg != null && koMsg.Contains("打劫"), "Message should mention 打劫");

        // White plays a ko threat elsewhere
        rules.Play(6, 6, out _); // W elsewhere
        rules.Play(7, 7, out _); // B answers elsewhere

        // Now White can retake the ko!
        bool wRetakeAllowed = rules.Play(1, 1, out string retakeMsg);
        Assert(wRetakeAllowed, "White retaking ko after threat should succeed: " + retakeMsg);
        Assert(rules.Board[rules.Idx(2, 1)] == GoRules.EMPTY, "B at (2,1) should be captured in return");
    }

    public static void TestConsecutivePassesAndScoring()
    {
        var rules = new GoRules(9);
        rules.Pass();
        Assert(rules.Passes == 1, "Passes should be 1");
        Assert(rules.Phase == GoPhase.Play, "Phase should still be Play");

        rules.Pass();
        Assert(rules.Passes == 2, "Passes should be 2");
        Assert(rules.Phase == GoPhase.Scoring, "Phase should be Scoring after 2 consecutive passes");

        rules.ResumePlay();
        Assert(rules.Phase == GoPhase.Play, "Phase should return to Play after ResumePlay");
        Assert(rules.Passes == 0, "Passes should be reset to 0");

        rules.Pass();
        rules.Pass();
        rules.ConfirmEnd();
        Assert(rules.Phase == GoPhase.Over, "Phase should be Over after ConfirmEnd");
        Assert(!string.IsNullOrEmpty(rules.Result), "Result string should be set");
    }

    public static void TestUndoBoardAndHash()
    {
        var rules = new GoRules(9);
        long initialHash = rules.Hash(rules.Board);

        rules.Play(3, 3, out _);
        rules.Play(3, 4, out _);
        rules.Play(4, 4, out _);

        Assert(rules.MoveCount == 3, "Move count should be 3");

        rules.Undo(1);
        Assert(rules.MoveCount == 2, "Move count should be 2 after 1 undo");
        Assert(rules.Board[rules.Idx(4, 4)] == GoRules.EMPTY, "(4,4) should be empty after undo");
        Assert(rules.Turn == GoRules.BLACK, "Turn should be BLACK after undoing white move");

        rules.Undo(2);
        Assert(rules.MoveCount == 0, "Move count should be 0 after undoing all moves");
        Assert(rules.Board[rules.Idx(3, 3)] == GoRules.EMPTY, "Board should be empty");
        Assert(rules.RepeatsPosition(rules.Board), "Initial empty board should be in posHashes");
        Assert(rules.ExportHashes().Contains(initialHash), "ExportHashes must contain initialHash");
    }

    public static void TestHandicapInitializationAndUndoWithKo()
    {
        var rules = new GoRules(9);
        int[] starPts = GoRules.StarPoints(9);
        int p1 = starPts[0] + starPts[2] * 9;
        int p2 = starPts[2] + starPts[0] * 9;
        int[] handicap = new int[] { p1, p2 };

        rules.SetupHandicap(handicap);
        long handicapHash = rules.Hash(rules.Board);

        Assert(rules.Turn == GoRules.WHITE, "Handicap game should have WHITE move first");
        Assert(rules.Board[p1] == GoRules.BLACK, "Handicap stone 1 must be BLACK");
        Assert(rules.Board[p2] == GoRules.BLACK, "Handicap stone 2 must be BLACK");
        Assert(rules.MoveCount == 0, "Initial MoveCount in handicap must be 0");
        Assert(rules.InitialHash == handicapHash, "InitialHash must equal handicap board hash");

        // Play 2 moves
        rules.Play(4, 4, out _); // W
        rules.Play(4, 5, out _); // B

        Assert(rules.MoveCount == 2, "Move count should be 2");

        // Undo back to handicap state
        rules.Undo(2);

        Assert(rules.MoveCount == 0, "Move count should be 0 after undoing 2 moves");
        Assert(rules.Turn == GoRules.WHITE, "Turn should be WHITE after undoing back to start");
        Assert(rules.Board[p1] == GoRules.BLACK, "Handicap stone 1 must still be present");
        Assert(rules.Board[p2] == GoRules.BLACK, "Handicap stone 2 must still be present");
        Assert(rules.Board[rules.Idx(4, 4)] == GoRules.EMPTY, "(4,4) must be empty");

        // CRITICAL: verify that posHashes contains handicapHash, NOT an empty board hash!
        Assert(rules.ExportHashes().Contains(handicapHash), "posHashes must contain handicap board hash after Undo");
        int[] emptyBoard = new int[81];
        Assert(!rules.ExportHashes().Contains(rules.Hash(emptyBoard)), "posHashes must NOT contain empty board hash in handicap game");

        // White can legally play (4,4) again
        bool wPlayOk = rules.Play(4, 4, out string wMsg);
        Assert(wPlayOk, "White should be able to play after undo: " + wMsg);
    }

    public static void TestScoringAndKomi()
    {
        var rules = new GoRules(9);
        rules.Komi = 5.5f;

        // Black takes one corner, White takes opposite corner
        rules.Play(0, 0, out _); // B
        rules.Play(8, 8, out _); // W
        rules.Pass();
        rules.Pass();

        rules.ComputeScore(out float bs, out float ws, out int bStones, out int bTerr, out int wStones, out int wTerr);
        Assert(bStones == 1, "Black stones should be 1");
        Assert(wStones == 1, "White stones should be 1");
        Assert(Mathf.Approximately(ws, wStones + wTerr + 5.5f), "White score must include Komi 5.5");
    }

    public static void TestStateDataExportImport()
    {
        var rules = new GoRules(9);
        rules.SetupHandicap(new[] { 20, 60 });
        rules.Play(4, 4, out _); // W
        rules.Play(4, 5, out _); // B

        var data = rules.ExportState();
        Assert(data.schemaVersion >= 1, "Exported state must have schemaVersion >= 1");
        Assert(data.size == 9, "Exported state size must be 9");
        Assert(data.moveCount == 2, "Exported state moveCount must be 2");

        var restored = new GoRules(9);
        bool ok = restored.ImportState(data);
        Assert(ok, "ImportState must succeed");
        Assert(restored.Size == 9, "Restored size must be 9");
        Assert(restored.Board[rules.Idx(4, 4)] == GoRules.WHITE, "Restored board (4,4) must be WHITE");
        Assert(restored.Board[rules.Idx(4, 5)] == GoRules.BLACK, "Restored board (4,5) must be BLACK");
        Assert(restored.Board[20] == GoRules.BLACK, "Handicap stone 20 must be restored");
        Assert(restored.Board[60] == GoRules.BLACK, "Handicap stone 60 must be restored");
        Assert(restored.MoveCount == 2, "Restored moveCount must be 2");
    }

    public static void TestReplayToPly()
    {
        var rules = new GoRules(9);
        rules.Play(2, 2, out _);
        rules.Play(6, 6, out _);
        rules.Play(2, 6, out _);
        string csv = rules.ExportMoveLogCsv();

        var replay = new GoRules(9);
        bool ok = replay.ReplayToPly(csv, 2);
        Assert(ok, "Replay to ply 2 must succeed");
        Assert(replay.MoveCount == 2, "MoveCount must be 2");
        Assert(replay.Board[rules.Idx(2, 2)] == GoRules.BLACK, "(2,2) must be BLACK");
        Assert(replay.Board[rules.Idx(6, 6)] == GoRules.WHITE, "(6,6) must be WHITE");
        Assert(replay.Board[rules.Idx(2, 6)] == GoRules.EMPTY, "(2,6) must still be EMPTY at ply 2");

        // Replay forward to ply 3
        replay.ReplayToPly(csv, 3);
        Assert(replay.MoveCount == 3, "MoveCount must be 3");
        Assert(replay.Board[rules.Idx(2, 6)] == GoRules.BLACK, "(2,6) must now be BLACK");
    }
}
