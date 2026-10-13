using System;
using UnityEditor;
using UnityEngine;

public static class TestRunner
{
    [MenuItem("Tools/Run All Tests")]
    public static void RunAllTests()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("================ STARTING TEST SUITE ================");
        Debug.Log("================ STARTING TEST SUITE ================");
        bool allPassed = true;

        try
        {
            GoRulesTests.RunAll();
            report.AppendLine("[GoRulesTests] All rule tests PASSED.");
        }
        catch (Exception ex)
        {
            allPassed = false;
            report.AppendLine("[GoRulesTests] FAILED: " + ex);
            Debug.LogError("[GoRulesTests] FAILED: " + ex);
        }

        try
        {
            GoSaveTests.RunAll();
            report.AppendLine("[GoSaveTests] All save tests PASSED.");
        }
        catch (Exception ex)
        {
            allPassed = false;
            report.AppendLine("[GoSaveTests] FAILED: " + ex);
            Debug.LogError("[GoSaveTests] FAILED: " + ex);
        }

        try
        {
            GoAiTests.RunAll();
            report.AppendLine("[GoAiTests] All AI tests PASSED.");
        }
        catch (Exception ex)
        {
            allPassed = false;
            report.AppendLine("[GoAiTests] FAILED: " + ex);
            Debug.LogError("[GoAiTests] FAILED: " + ex);
        }

        string resultPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../test_result.txt"));
        if (allPassed)
        {
            report.AppendLine("================ ALL TESTS PASSED SUCCESSFULLY! ================");
            Debug.Log("================ ALL TESTS PASSED SUCCESSFULLY! ================");
            System.IO.File.WriteAllText(resultPath, report.ToString());
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        else
        {
            report.AppendLine("================ SOME TESTS FAILED! ================");
            Debug.LogError("================ SOME TESTS FAILED! ================");
            System.IO.File.WriteAllText(resultPath, report.ToString());
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
