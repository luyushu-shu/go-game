using System.Collections.Generic;
using UnityEngine;

public static class GoIconCatalog
{
    public const string Settings = GoRuntimeAssetManifest.SettingsIconKey;
    public const string Volume = GoRuntimeAssetManifest.VolumeIconKey;
    public const string Mute = GoRuntimeAssetManifest.MuteIconKey;
    public const string Undo = GoRuntimeAssetManifest.UndoIconKey;
    public const string Pass = GoRuntimeAssetManifest.PassIconKey;
    public const string Score = GoRuntimeAssetManifest.ScoreIconKey;
    public const string Resign = GoRuntimeAssetManifest.ResignIconKey;
    public const string BoardMoveNumbers = GoRuntimeAssetManifest.BoardMoveNumbersKey;
    public const string BoardCoordinates = GoRuntimeAssetManifest.BoardCoordinatesKey;
    public const string BoardSituation = GoRuntimeAssetManifest.BoardSituationKey;

    static readonly Dictionary<string, Sprite> fallbacks = new Dictionary<string, Sprite>();

    public static Sprite Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var sprite = GoRuntimeAssetManifest.Instance != null
            ? GoRuntimeAssetManifest.Instance.GetSprite(key) : null;
        if (sprite != null) return sprite;
        sprite = Resources.Load<Sprite>(key);
        if (sprite != null) return sprite;

        // Accept an asset key without the Resources/ prefix as well.
        if (key.StartsWith("UI/Icons/"))
            sprite = Resources.Load<Sprite>(key.Substring("UI/Icons/".Length));
        else
            sprite = Resources.Load<Sprite>("UI/Icons/" + key);
        if (sprite != null) return sprite;

        Sprite fallback;
        if (fallbacks.TryGetValue(key, out fallback)) return fallback;
        Debug.LogError("Missing runtime icon sprite: " + key);
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = key + "_RuntimeFallback",
            hideFlags = HideFlags.HideAndDontSave
        };
        int seed = Mathf.Abs(key.GetHashCode());
        Color tint = Color.HSVToRGB((seed % 360) / 360f, 0.45f, 0.85f);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            bool edge = x < 2 || x >= 30 || y < 2 || y >= 30;
            bool mark = (x == y || x == 31 - y || x == 16 || y == 16);
            texture.SetPixel(x, y, edge || mark ? Color.white : tint);
        }
        texture.Apply();
        fallback = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f),
            new Vector2(0.5f, 0.5f), 32f);
        fallback.name = key + "_RuntimeFallback";
        fallback.hideFlags = HideFlags.HideAndDontSave;
        fallbacks[key] = fallback;
        return fallback;
    }
}
