using UnityEditor;
using UnityEditor.Build.Reporting;

public static class StudyArtBuild
{
    public static void BuildPreview()
    {
        AssetDatabase.Refresh();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Builds/StudyUiPreview/GoGame.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
