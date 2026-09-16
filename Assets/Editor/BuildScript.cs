using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    [MenuItem("Build/Windows EXE")]
    public static void BuildWindows()
    {
        PlayerSettings.productName = "围棋";
        PlayerSettings.companyName = "luyus";

        var opts = new BuildPlayerOptions
        {
            scenes = new string[0],
            locationPathName = "Builds/Windows/GoGame.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log("BUILD OK -> " + opts.locationPathName);
        }
        else
        {
            Debug.LogError("BUILD FAILED: " + report.summary.result);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
