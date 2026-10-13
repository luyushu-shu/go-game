using UnityEditor;
using UnityEditor.Build;
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
        AssetDatabase.Refresh();

        // 工程靠运行时代码自举，场景本身为空；但构建必须包含一个已保存的场景文件
        const string sceneDir = "Assets/Scenes";
        const string scenePath = sceneDir + "/Main.unity";
        if (!AssetDatabase.IsValidFolder(sceneDir))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var manifestObject = new GameObject("QiyiRuntimeAssetManifest");
        var manifest = manifestObject.AddComponent<GoRuntimeAssetManifest>();
        manifest.AssignAssets(
            LoadRequired<Sprite>("Assets/Art/UI/Background/qiyi_ink_room_2048.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Background/qiyi_mountain_silhouette_2048.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Background/qiyi_bamboo_shadow_1024.png"),
            LoadRequired<Sprite>("Assets/Art/Board/board_wood_1024.png"),
            LoadRequired<Sprite>("Assets/Art/Board/stone_black_256.png"),
            LoadRequired<Sprite>("Assets/Art/Board/stone_white_256.png"),
            LoadRequired<Sprite>("Assets/Art/Board/stone_shadow_256.png"),
            LoadRequired<Sprite>("Assets/Art/Board/marker_last_move_128.png"),
            LoadRequired<Sprite>("Assets/Art/Board/marker_ai_best_128.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Panels/panel_paper_9slice_512.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Panels/panel_ink_9slice_512.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Buttons/button_icon_96.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Icons/icon_board_moves_64.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Icons/icon_board_coordinates_64.png"),
            LoadRequired<Sprite>("Assets/Art/UI/Icons/icon_board_situation_64.png"),
            LoadRequired<AudioClip>("Assets/Art/Audio/button_click.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/button_confirm.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/stone_place.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/stone_capture.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/clock_warning.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/game_end.wav"),
            LoadRequired<AudioClip>("Assets/Art/Audio/bgm_study.ogg"),
            LoadRequired<AudioClip>("Assets/Art/Audio/bgm_review.ogg"));
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

    static T LoadRequired<T>(string assetPath) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
            throw new BuildFailedException("Missing required Qiyi runtime asset: " + assetPath);
        return asset;
    }
}
