using NUnit.Framework;
using UnityEngine;

public class GoBackgroundMusicTests
{
    [TestCase("bgm_menu")]
    [TestCase("bgm_study_song")]
    [TestCase("bgm_ai_match")]
    public void BackgroundTrackLoadsThroughRuntimeLoaderAndMusicSourceLoops(string key)
    {
        var host = new GameObject("BackgroundMusicTest");
        try
        {
            var audio = host.AddComponent<GoAudioManager>();
            // Awake is not invoked automatically by EditMode component creation.
            if (GoAudioManager.Instance != audio)
                typeof(GoAudioManager).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(audio, null);
            // EditMode cannot play AudioSources; exercise the same runtime loader.
            var loader = typeof(GoAudioManager).GetMethod("LoadClip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var clip = (AudioClip)loader.Invoke(audio, new object[] { key });
            var source = host.transform.Find("MusicSource").GetComponent<AudioSource>();
            Assert.NotNull(clip, "Runtime loader could not resolve " + key);
            Assert.AreEqual(key, clip.name);
            Assert.That(clip.samples, Is.GreaterThan(0));
            Assert.AreEqual(48000, clip.frequency);
            Assert.AreEqual(2, clip.channels);
            Assert.IsTrue(source.loop);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }
}
