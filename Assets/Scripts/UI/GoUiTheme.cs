using UnityEngine;

public enum GoUiPage { Splash, MainMenu, AiSetup, Settings, Playing, Study, History, Review }
public enum GoButtonStyle { Primary, Secondary, Danger, Icon }

public sealed class GoUiTheme : ScriptableObject
{
    public Color Ink900 = new Color(0.090f, 0.075f, 0.059f, 1f);
    public Color Ink800 = new Color(0.129f, 0.102f, 0.078f, 1f);
    public Color Ink700 = new Color(0.169f, 0.129f, 0.094f, 1f);
    public Color Wood900 = new Color(0.227f, 0.141f, 0.075f, 1f);
    public Color Wood500 = new Color(0.659f, 0.459f, 0.231f, 1f);
    public Color Paper = new Color(0.949f, 0.910f, 0.820f, 1f);
    public Color PaperDim = new Color(0.725f, 0.667f, 0.549f, 1f);
    public Color Gold = new Color(0.827f, 0.635f, 0.298f, 1f);
    public Color Cinnabar = new Color(0.714f, 0.290f, 0.227f, 1f);
    public Color Jade = new Color(0.435f, 0.604f, 0.510f, 1f);
    public float PageTransitionSeconds = 0.220f;
    public float PanelExpandSeconds = 0.180f;
    public float ButtonHoverSeconds = 0.120f;

    // Kept as a runtime-friendly fallback until the approved font asset is introduced.
    public Font FallbackFont;

    public static GoUiTheme CreateRuntimeDefault(Font fallback)
    {
        var theme = CreateInstance<GoUiTheme>();
        theme.FallbackFont = fallback != null ? fallback : LoadFallbackFont();
        return theme;
    }

    static Font LoadFallbackFont()
    {
        var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtin != null) return builtin;

        try
        {
            return Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
        }
        catch
        {
            return null;
        }
    }
}
