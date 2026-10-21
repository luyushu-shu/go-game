using UnityEditor;
using UnityEditor.Build.Reporting;

public static class AiArtBuild
{
    public static void BuildPreview()
    {
        AssetDatabase.Refresh();
        ConfigureTextures();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Builds/AiUiPreview/GoGame.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    [MenuItem("Tools/AI Match/Configure Art Textures")]
    public static void ConfigureTextures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI/AiMatch" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
