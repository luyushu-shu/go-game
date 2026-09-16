using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildScript
{
    [MenuItem("Build/Windows EXE")]
    public static void BuildWindows()
    {
        PlayerSettings.productName = "围棋";
        PlayerSettings.companyName = "luyus";

        // 工程靠运行时代码自举，场景本身为空；但构建必须包含一个已保存的场景文件
        const string sceneDir = "Assets/Scenes";
        const string scenePath = sceneDir + "/Main.unity";
        if (!AssetDatabase.IsValidFolder(sceneDir))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
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
