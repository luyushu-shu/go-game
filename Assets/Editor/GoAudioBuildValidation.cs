using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Cover every build entry point, including builds started from the editor UI.
public sealed class GoAudioBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        foreach (var key in new[] { "bgm_menu", "bgm_study_song", "bgm_ai_match" })
        {
            string path = "Assets/Resources/Audio/" + key + ".wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null || clip.samples <= 0 || clip.frequency != 48000 || clip.channels != 2)
                throw new BuildFailedException("Missing or invalid background music: " + path
                    + ". Expected a nonempty 48 kHz stereo clip.");
            Debug.Log("BGM validated: " + key + " (" + clip.length + " seconds)");
        }
    }
}
