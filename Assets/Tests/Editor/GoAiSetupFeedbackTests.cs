using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class GoAiSetupFeedbackTests
{
    GameObject host, setup, gameplay;
    GoGame game;
    Text feedback;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("InactiveGame");
        host.SetActive(false); // Do not bootstrap the full game in EditMode.
        game = host.AddComponent<GoGame>();
        setup = new GameObject("VisibleSetup");
        gameplay = new GameObject("HiddenGameplay");
        gameplay.SetActive(false);
        feedback = new GameObject("SetupFeedback", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        feedback.transform.SetParent(setup.transform);
        Set("aiSetupPanel", setup);
        Set("aiSetupErrorText", feedback);
        Set("gameUiPanel", gameplay);
        // No gameplay message label: launch errors must not access it.
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(host);
        Object.DestroyImmediate(setup);
        Object.DestroyImmediate(gameplay);
    }

    [TestCase("KataGo 尚未就绪，请安装引擎或选择本地引擎")]
    [TestCase("请先填写大模型密钥")]
    public void LaunchFeedbackRemainsVisibleWhileGameplayIsHidden(string message)
    {
        Invoke("Flash", message);
        Assert.AreEqual(message, feedback.text);
        Assert.IsTrue(feedback.gameObject.activeInHierarchy);
        Assert.IsFalse(gameplay.activeInHierarchy);
        Assert.IsNull(typeof(GoGame).GetField("msgCo", PrivateInstance).GetValue(game));
    }

    [Test]
    public void InvalidCustomTimeStopsLaunchWithVisibleFeedback()
    {
        var control = GoTimeControl.FromPreset(GoTimePreset.Unlimited, GoByoyomiPreset.None);
        control.MainPreset = GoTimePreset.Custom;
        Set("timeControl", control);
        Set("customMainMinutesText", "0");
        Invoke("StartAiGame");
        Assert.AreEqual("请先修正时间设置", feedback.text);
        Assert.IsTrue(feedback.gameObject.activeInHierarchy);
        Assert.IsFalse(gameplay.activeInHierarchy);
    }

    void Set(string name, object value) => typeof(GoGame).GetField(name, PrivateInstance).SetValue(game, value);
    void Invoke(string name, params object[] args) => typeof(GoGame).GetMethod(name, PrivateInstance).Invoke(game, args);
}
