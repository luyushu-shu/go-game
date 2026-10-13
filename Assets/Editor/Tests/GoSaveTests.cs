using System;
using System.IO;
using UnityEngine;

public static class GoSaveTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Assertion Failed: " + message);
    }

    public static void RunAll()
    {
        TestKifuRecordValidation();
        TestKifuRecordAddAndQuery();
        TestKifuMigration();
        TestStudyProgressSaveAndLoad();
        TestTerminalStudyProgressPreservesResult();
        TestStudySaveValidation();
        Debug.Log("[GoSaveTests] All 6 persistence tests passed successfully!");
    }

    public static void TestKifuRecordValidation()
    {
        bool okNull = KifuDatabase.ValidateRecord(null, out string errNull);
        Assert(!okNull && !string.IsNullOrEmpty(errNull), "Null record should fail validation");

        var invalidSize = new KifuRecord { boardSize = 50, title = "test" };
        bool okSize = KifuDatabase.ValidateRecord(invalidSize, out string errSize);
        Assert(!okSize && !string.IsNullOrEmpty(errSize), "Invalid board size should fail validation");

        var valid = new KifuRecord { boardSize = 19, title = "Valid Game" };
        bool okValid = KifuDatabase.ValidateRecord(valid, out _);
        Assert(okValid, "Valid record should pass validation");
    }

    public static void TestKifuRecordAddAndQuery()
    {
        string uniqueId = "test_" + Guid.NewGuid().ToString("N");
        string uniqueTitle = "Unit Test Game " + uniqueId;
        var rec = new KifuRecord
        {
            id = uniqueId,
            title = uniqueTitle,
            blackName = "TestBlack",
            whiteName = "TestWhite",
            boardSize = 19,
            playedAt = "2026-10-12",
            result = "黑中盘胜"
        };

        bool ok = KifuDatabase.TryAdd(rec, out string err);
        Assert(ok, "TryAdd should succeed: " + err);

        var queryResult = KifuDatabase.Query(uniqueId);
        Assert(queryResult != null && queryResult.Count >= 1, "Query by unique id should find the record");
        Assert(queryResult[0].title == uniqueTitle, "Record title must match");
        Assert(queryResult[0].schemaVersion >= 1, "schemaVersion must be >= 1");
    }

    public static void TestKifuMigration()
    {
        var legacyRec = new KifuRecord
        {
            schemaVersion = 0,
            boardSize = 0, // legacy unset size
            title = "Legacy Game"
        };

        bool ok = KifuDatabase.TryAdd(legacyRec, out _);
        Assert(ok, "Adding legacy record should succeed and auto-migrate");
        Assert(legacyRec.schemaVersion >= 1, "schemaVersion should be migrated to >= 1");
    }

    public static void TestStudyProgressSaveAndLoad()
    {
        var rules = new GoRules(9);
        rules.Play(3, 3, out _);
        rules.Play(3, 4, out _);

        bool saveOk = GoSaveManager.SaveStudyProgress(rules, out string saveErr);
        Assert(saveOk, "SaveStudyProgress should succeed: " + saveErr);
        Assert(File.Exists(GoSaveManager.StudySavePath), "study_save.json file should exist");

        bool loadOk = GoSaveManager.TryLoadStudySave(out GoStateData loaded, out string loadMsg);
        Assert(loadOk, "TryLoadStudySave should succeed: " + loadMsg);
        Assert(loaded != null, "Loaded data must not be null");
        Assert(loaded.size == 9, "Loaded size must be 9");
        Assert(loaded.moveCount == 2, "Loaded moveCount must be 2");

        GoSaveManager.ClearStudySave();
        Assert(!File.Exists(GoSaveManager.StudySavePath), "study_save.json should be deleted after clear");
        Assert(!PlayerPrefs.HasKey(GoSaveManager.StudySaveKey), "PlayerPrefs key should be deleted after clear");
    }

    public static void TestStudySaveValidation()
    {
        var badData = new GoStateData
        {
            size = 99
        };
        bool ok = GoSaveManager.ValidateStateData(badData, out string err);
        Assert(!ok && !string.IsNullOrEmpty(err), "Invalid state size should fail validation");
    }

    public static void TestTerminalStudyProgressPreservesResult()
    {
        var rules = new GoRules(9);
        rules.Pass();
        rules.Pass();
        rules.ConfirmEnd();
        string expected = rules.Result;

        bool saveOk = GoSaveManager.SaveStudyTerminalProgress(rules, out string saveErr);
        Assert(saveOk, "SaveStudyTerminalProgress should succeed: " + saveErr);
        Assert(GoSaveManager.TryLoadStudySave(out GoStateData loaded, out string loadMsg),
            "Terminal study save should load: " + loadMsg);
        Assert(loaded.phase == (int)GoPhase.Over, "Terminal study save must preserve Over phase");
        Assert(loaded.result == expected, "Terminal study save must preserve result");

        GoSaveManager.ClearStudySave();
    }
}
