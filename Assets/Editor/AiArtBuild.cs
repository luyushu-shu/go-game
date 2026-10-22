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
        if (report.summary.result == BuildResult.Succeeded)
            CopyLocalKataGoForPreview("Builds/AiUiPreview/Tools/KataGo");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // Local previews use the engine already installed in this project.
    // Missing files still surface through the runtime launch guard.
    public static void CopyLocalKataGoForPreview(string destination)
    {
        if (!System.IO.Directory.Exists("Tools/KataGo")) return;
        System.IO.Directory.CreateDirectory(destination);
        foreach (string source in System.IO.Directory.GetFiles("Tools/KataGo"))
        {
            string extension = System.IO.Path.GetExtension(source);
            if (extension == ".exe" || extension == ".dll" || extension == ".gz" || extension == ".cfg" || extension == ".txt")
                System.IO.File.Copy(source, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(source)), true);
        }
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
