using UnityEngine;

public enum GoUiPage { Splash, MainMenu, AiSetup, Settings, Playing, Study, History, Review }
public enum GoButtonStyle { Primary, Secondary, Danger, Icon }

public sealed class GoUiTheme : ScriptableObject
{
    public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    // These are physical-pixel targets at the reference resolution. Layout
    // code must convert them through the active CanvasScaler instead of using
    // the values as raw Canvas units.
    public const float SafeAreaMarginPixelsX = 120f;
    public const float SafeAreaMarginPixelsY = 70f;
    public const float MinimumBodyFontPixels = 13f;
    public const float CompactSidebarPixels = 360f;
    public const float DesktopSidebarPixels = 360f;

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

    public static float CanvasScaleForScreen(float screenWidth, float screenHeight)
    {
        float width = Mathf.Max(1f, screenWidth) / ReferenceResolution.x;
        float height = Mathf.Max(1f, screenHeight) / ReferenceResolution.y;
        // MatchWidthOrHeight uses logarithmic interpolation, which is the
        // geometric mean for the current match=0.5 configuration.
        return Mathf.Pow(width, 0.5f) * Mathf.Pow(height, 0.5f);
    }

    public static float CanvasUnitsForPixels(float physicalPixels,
        float screenWidth, float screenHeight)
    {
        return Mathf.Max(0f, physicalPixels)
            / Mathf.Max(0.0001f, CanvasScaleForScreen(screenWidth, screenHeight));
    }

    public static float PhysicalPixelsForCanvasUnits(float canvasUnits,
        float screenWidth, float screenHeight)
    {
        return Mathf.Max(0f, canvasUnits) * CanvasScaleForScreen(screenWidth, screenHeight);
    }

    public static float SafeAreaMarginCanvasX(float screenWidth, float screenHeight)
    {
        // Keep the title-safe margin proportional to the display while
        // preserving the reference design's 120px margin at 1920x1080.
        float targetPixels = SafeAreaMarginPixelsX
            * Mathf.Max(1f, screenWidth) / ReferenceResolution.x;
        return CanvasUnitsForPixels(targetPixels, screenWidth, screenHeight);
    }

    public static float SafeAreaMarginCanvasY(float screenWidth, float screenHeight)
    {
        float targetPixels = SafeAreaMarginPixelsY
            * Mathf.Max(1f, screenHeight) / ReferenceResolution.y;
        return CanvasUnitsForPixels(targetPixels, screenWidth, screenHeight);
    }

    public static float MinimumBodyFontSizeCanvas(float screenWidth, float screenHeight)
    {
        return CanvasUnitsForPixels(MinimumBodyFontPixels, screenWidth, screenHeight);
    }

    public static int MinimumBodyFontSizeForScreen(float screenWidth, float screenHeight)
    {
        return Mathf.CeilToInt(MinimumBodyFontSizeCanvas(screenWidth, screenHeight));
    }

    public static int MinimumBodyFontSizeForCanvas(Canvas canvas)
    {
        if (canvas != null && canvas.scaleFactor > 0.0001f)
            return Mathf.CeilToInt(MinimumBodyFontPixels / canvas.scaleFactor);
        return MinimumBodyFontSizeForScreen(Screen.width, Screen.height);
    }

    public static float SidebarWidthCanvas(float screenWidth, float screenHeight)
    {
        float width = Mathf.Max(1f, screenWidth);
        float targetPixels;
        if (width <= 1366f) targetPixels = CompactSidebarPixels;
        else if (width >= 1920f) targetPixels = DesktopSidebarPixels;
        else targetPixels = Mathf.Lerp(CompactSidebarPixels, DesktopSidebarPixels,
            Mathf.InverseLerp(1366f, 1920f, width));
        return CanvasUnitsForPixels(targetPixels, width, screenHeight);
    }

    public static bool ResponsiveTargetsHold(float screenWidth, float screenHeight)
    {
        float sidebarPixels = PhysicalPixelsForCanvasUnits(
            SidebarWidthCanvas(screenWidth, screenHeight), screenWidth, screenHeight);
        float bodyPixels = PhysicalPixelsForCanvasUnits(
            MinimumBodyFontSizeCanvas(screenWidth, screenHeight), screenWidth, screenHeight);
        return sidebarPixels >= 340f && sidebarPixels <= 390f
            && bodyPixels + 0.001f >= MinimumBodyFontPixels;
    }

    public static void AssertResponsiveTargets(float screenWidth, float screenHeight)
    {
        Debug.Assert(ResponsiveTargetsHold(screenWidth, screenHeight),
            $"Qiyi responsive targets failed at {screenWidth}x{screenHeight}");
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
