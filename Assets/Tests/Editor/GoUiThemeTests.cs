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

    static void AssertButtonNormalColor(GoUiFactory factory, Transform parent,
        GoButtonStyle style, Color expected)
    {
        var button = factory.MakeButton(parent, style + "Button", "Label", style, null);
        var image = (Image)button.targetGraphic;
        Assert.AreEqual(expected, image.color);
        Assert.AreEqual(expected, button.colors.normalColor);
    }
}
