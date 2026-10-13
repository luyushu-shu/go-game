using NUnit.Framework;
using UnityEngine;

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
}
