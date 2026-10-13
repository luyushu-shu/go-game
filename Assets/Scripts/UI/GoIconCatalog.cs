using System.Collections.Generic;
using UnityEngine;

public static class GoIconCatalog
{
    public const string Settings = "UI/Icons/icon_settings_64";
    public const string Volume = "UI/Icons/icon_volume_64";
    public const string Mute = "UI/Icons/icon_mute_64";
    public const string Undo = "UI/Icons/icon_undo_64";
    public const string Pass = "UI/Icons/icon_pass_64";
    public const string Score = "UI/Icons/icon_score_64";
    public const string Resign = "UI/Icons/icon_resign_64";

    static readonly Dictionary<string, Sprite> fallbacks = new Dictionary<string, Sprite>();

    public static Sprite Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var sprite = Resources.Load<Sprite>(key);
        if (sprite != null) return sprite;

        // Accept an asset key without the Resources/ prefix as well.
        if (key.StartsWith("UI/Icons/"))
            sprite = Resources.Load<Sprite>(key.Substring("UI/Icons/".Length));
        else
            sprite = Resources.Load<Sprite>("UI/Icons/" + key);
        if (sprite != null) return sprite;

        Sprite fallback;
        if (fallbacks.TryGetValue(key, out fallback)) return fallback;
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = key + "_RuntimeFallback",
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        fallback = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f), 1f);
        fallback.name = key + "_RuntimeFallback";
        fallback.hideFlags = HideFlags.HideAndDontSave;
        fallbacks[key] = fallback;
        return fallback;
    }
}
