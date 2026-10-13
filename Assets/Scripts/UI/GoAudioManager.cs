using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime audio service for the Qiyi Youdao shell.
///
/// Audio is deliberately optional during the migration: clips are loaded when
/// they are available, while missing art/audio leaves the game fully usable.
/// </summary>
public sealed class GoAudioManager : MonoBehaviour
{
    public const string MasterKey = "Qiyi.MasterVolume";
    public const string MusicKey = "Qiyi.MusicVolume";
    public const string SfxKey = "Qiyi.SfxVolume";
    public const string WarningKey = "Qiyi.WarningVolume";
    public const string MuteKey = "Qiyi.Mute";

    public const float DefaultMasterVolume = 0.70f;
    public const float DefaultMusicVolume = 0.25f;
    public const float DefaultSfxVolume = 0.65f;
    public const float DefaultWarningVolume = 0.80f;

    public static GoAudioManager Instance { get; private set; }

    readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    AudioSource musicSource;
    AudioSource sfxSource;
    string currentMusicKey;
    float masterVolume = DefaultMasterVolume;
    float musicVolume = DefaultMusicVolume;
    float sfxVolume = DefaultSfxVolume;
    float warningVolume = DefaultWarningVolume;
    bool muted;

    public float MasterVolume => masterVolume;
    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;
    public float WarningVolume => warningVolume;
    public bool IsMuted => muted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        musicSource = CreateSource("MusicSource");
        sfxSource = CreateSource("SfxSource");
        musicSource.loop = true;
        LoadPrefs();
    }

    AudioSource CreateSource(string objectName)
    {
        var sourceObject = new GameObject(objectName);
        sourceObject.transform.SetParent(transform, false);
        var source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        return source;
    }

    public void LoadPrefs()
    {
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, DefaultMasterVolume));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusicVolume));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultSfxVolume));
        warningVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(WarningKey, DefaultWarningVolume));
        muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;
        ApplyVolumes();
    }

    public void SavePrefs()
    {
        PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(masterVolume));
        PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(musicVolume));
        PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(sfxVolume));
        PlayerPrefs.SetFloat(WarningKey, Mathf.Clamp01(warningVolume));
        PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetWarningVolume(float value)
    {
        warningVolume = Mathf.Clamp01(value);
    }

    public void SetMute(bool value)
    {
        muted = value;
        ApplyVolumes();
    }

    public void PlaySfx(string key)
    {
        if (sfxSource == null || string.IsNullOrEmpty(key)) return;

        var clip = LoadClip(key);
        if (clip == null) return;

        float categoryVolume = key == "clock_warning" ? warningVolume : sfxVolume;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(categoryVolume));
    }

    public void PlayMusic(string key)
    {
        if (musicSource == null || string.IsNullOrEmpty(key)) return;
        if (currentMusicKey == key && musicSource.isPlaying) return;

        var clip = LoadClip(key);
        if (clip == null) return;

        currentMusicKey = key;
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    void ApplyVolumes()
    {
        float muteMultiplier = muted ? 0f : 1f;
        if (musicSource != null)
            musicSource.volume = Mathf.Clamp01(masterVolume * musicVolume) * muteMultiplier;
        if (sfxSource != null)
            sfxSource.volume = Mathf.Clamp01(masterVolume) * muteMultiplier;
    }

    AudioClip LoadClip(string key)
    {
        AudioClip clip;
        if (clipCache.TryGetValue(key, out clip)) return clip;

        if (GoRuntimeAssetManifest.Instance != null)
            clip = GoRuntimeAssetManifest.Instance.GetAudio(key);
        if (clip == null) clip = Resources.Load<AudioClip>("Art/Audio/" + key);
        if (clip == null) clip = Resources.Load<AudioClip>("Audio/" + key);

        if (clip != null) clipCache[key] = clip;
        return clip;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
