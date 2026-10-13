using System.IO;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class GoUiThemeTests
{
    [Test]
    public void RuntimeDefaultUsesApprovedPalette()
    {
        var theme = GoUiTheme.CreateRuntimeDefault(null);
        Assert.AreEqual(new Color(0.090f, 0.075f, 0.059f, 1f), theme.Ink900);
        Assert.AreEqual(new Color(0.949f, 0.910f, 0.820f, 1f), theme.Paper);
        Assert.AreEqual(new Color(0.827f, 0.635f, 0.298f, 1f), theme.Gold);
        Assert.AreEqual(0.220f, theme.PageTransitionSeconds, 0.0001f);
        Object.DestroyImmediate(theme);
    }

    [Test]
    public void ResponsiveTargetsKeepPhysicalBoundsAtReleaseResolutions()
    {
        foreach (var size in new[] { new Vector2(1920f, 1080f),
            new Vector2(1600f, 900f), new Vector2(1366f, 768f) })
        {
            Assert.IsTrue(GoUiTheme.ResponsiveTargetsHold(size.x, size.y),
                $"responsive UI target failed at {size.x}x{size.y}");
            float sidebar = GoUiTheme.PhysicalPixelsForCanvasUnits(
                GoUiTheme.SidebarWidthCanvas(size.x, size.y), size.x, size.y);
            float font = GoUiTheme.PhysicalPixelsForCanvasUnits(
                GoUiTheme.MinimumBodyFontSizeCanvas(size.x, size.y), size.x, size.y);
            Assert.That(sidebar, Is.InRange(340f, 390f));
            Assert.That(font, Is.GreaterThanOrEqualTo(GoUiTheme.MinimumBodyFontPixels));
        }
    }

    [Test]
    public void ButtonStylesUseThemeColorForNormalTransition()
    {
        var theme = GoUiTheme.CreateRuntimeDefault(null);
        var root = new GameObject("GoUiFactoryTestRoot");
        var factory = root.AddComponent<GoUiFactory>();
        factory.Theme = theme;

        try
        {
            AssertButtonNormalColor(factory, root.transform, GoButtonStyle.Primary, theme.Gold);
            AssertButtonNormalColor(factory, root.transform, GoButtonStyle.Secondary, theme.Ink700);
            AssertButtonNormalColor(factory, root.transform, GoButtonStyle.Danger, theme.Cinnabar);
            AssertButtonNormalColor(factory, root.transform, GoButtonStyle.Icon, theme.Ink800);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(theme);
        }
    }

    [Test]
    public void RequiredUiSpritesAreNonEmptySingleSpriteAssets()
    {
        string[] paths = {
            "Assets/Art/UI/Buttons/button_primary_9slice_256.png",
            "Assets/Art/UI/Buttons/button_secondary_9slice_256.png",
            "Assets/Art/UI/Buttons/button_danger_9slice_256.png",
            "Assets/Art/UI/Icons/icon_settings_64.png",
            "Assets/Art/UI/Icons/icon_volume_64.png",
            "Assets/Art/UI/Icons/icon_mute_64.png",
            "Assets/Art/UI/Icons/icon_undo_64.png",
            "Assets/Art/UI/Icons/icon_pass_64.png",
            "Assets/Art/UI/Icons/icon_score_64.png",
            "Assets/Art/UI/Icons/icon_resign_64.png"
        };

        foreach (var path in paths)
        {
            Assert.IsTrue(File.Exists(path), "Missing UI asset: " + path);
            Assert.Greater(new FileInfo(path).Length, 128, "Blank UI asset: " + path);
            string meta = File.ReadAllText(path + ".meta");
            StringAssert.Contains("spriteMode: 1", meta);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsNotNull(sprite, "Not imported as Sprite: " + path);
        }
    }

    static void AssertButtonNormalColor(GoUiFactory factory, Transform parent,
        GoButtonStyle style, Color expected)
    {
        var button = factory.MakeButton(parent, style + "Button", "Label", style, null);
        var image = (Image)button.targetGraphic;
        Assert.AreEqual(expected, image.color);
        Assert.AreEqual(expected, button.colors.normalColor);
    }
}
