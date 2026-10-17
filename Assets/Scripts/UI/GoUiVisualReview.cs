using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Opt-in rendering check for UI regressions; inactive in normal launches.
public sealed class GoUiVisualReview : MonoBehaviour
{
    string outputDirectory;
    readonly List<string> observations = new List<string>();
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        var args = Environment.GetCommandLineArgs();
        int option = Array.IndexOf(args, "--ui-review-dir");
        if (option < 0 || option + 1 >= args.Length) return;
        var probe = new GameObject("UiVisualReview").AddComponent<GoUiVisualReview>();
        probe.outputDirectory = Path.GetFullPath(args[option + 1]);
        DontDestroyOnLoad(probe.gameObject);
        Application.runInBackground = true;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(outputDirectory);
        for (int i = 0; i < 30; i++) yield return null;
        var game = FindObjectOfType<GoGame>();
        if (game == null) throw new InvalidOperationException("Game did not initialize.");
        Invoke(game, "ShowMainMenu");
        yield return new WaitForSecondsRealtime(0.4f);
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var menu = (GameObject)typeof(GoGame).GetField("mainPanel", PrivateInstance).GetValue(game);
        var actions = menu.transform.Find("MenuActions");
        if (actions == null) throw new InvalidOperationException("Compact menu group is missing.");
        for (int i = 1; i < actions.childCount; i++)
        {
            var previous = (RectTransform)actions.GetChild(i - 1);
            var current = (RectTransform)actions.GetChild(i);
            float gap = previous.anchoredPosition.y - previous.rect.height - current.anchoredPosition.y;
            observations.Add("Menu gap: " + gap.ToString("F2"));
            if (Mathf.Abs(gap - 8f) > 0.5f) throw new InvalidOperationException("Uneven menu spacing.");
        }
        var primary = actions.GetChild(0).GetComponent<Image>().sprite;
        if (Mathf.Abs(primary.rect.xMax - primary.texture.width) > 0.5f)
            throw new InvalidOperationException("Primary plaque right edge is cropped.");
        Capture("main-menu.png");

        Invoke(game, "ShowAiSetup");
        yield return new WaitForSecondsRealtime(0.4f);
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        Capture("ai-setup.png");
        var ai = (GameObject)typeof(GoGame).GetField("aiSetupPanel", PrivateInstance).GetValue(game);
        if (ai.GetComponent<Image>().sprite.texture.name != "ai_setup_paper")
            throw new InvalidOperationException("AI setup does not use its independent background.");
        foreach (var optionName in new[] { "optSize", "optColor", "optMainTime", "optByoyomi", "optOpponent" })
        {
            var options = (List<Button>)typeof(GoGame).GetField(optionName, PrivateInstance).GetValue(game);
            int selected = 0;
            foreach (var optionButton in options)
            {
                if (optionButton.GetComponent<Image>().sprite.texture.name == "ai_setup_option_selected") selected++;
                var label = optionButton.GetComponentInChildren<Text>();
                if (label.alignment != TextAnchor.MiddleCenter || !label.alignByGeometry)
                    throw new InvalidOperationException("AI setup option is not centered.");
            }
            if (selected != 1) throw new InvalidOperationException("AI setup selection is ambiguous: " + optionName);
            observations.Add("AI options: " + optionName + " centered, one selection");
        }
        var timeOptions = (List<Button>)typeof(GoGame).GetField("optMainTime", PrivateInstance).GetValue(game);
        var byoyomiOptions = (List<Button>)typeof(GoGame).GetField("optByoyomi", PrivateInstance).GetValue(game);
        int mainPreset = timeOptions.FindIndex(b => b.GetComponent<Image>().sprite.texture.name == "ai_setup_option_selected");
        int byoyomiPreset = byoyomiOptions.FindIndex(b => b.GetComponent<Image>().sprite.texture.name == "ai_setup_option_selected");
        var footer = ai.transform.Find("AiSetupFooter").GetComponent<RectTransform>();
        var footerPosition = footer.anchoredPosition;
        timeOptions[4].onClick.Invoke();
        byoyomiOptions[4].onClick.Invoke();
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        if (footer.anchoredPosition != footerPosition)
            throw new InvalidOperationException("AI footer moved when custom time expanded.");
        Capture("ai-setup-custom-time.png");
        timeOptions[mainPreset].onClick.Invoke();
        byoyomiOptions[byoyomiPreset].onClick.Invoke();

        Invoke(game, "ShowSettings");
        Invoke(game, "ShowSettingsSection", 3);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        var settings = (GameObject)typeof(GoGame).GetField("settingsPanel", PrivateInstance).GetValue(game);
        foreach (var text in settings.GetComponentsInChildren<Text>())
        {
            if (text.name != "Val") continue;
            if (!text.alignByGeometry || text.alignment != TextAnchor.MiddleCenter)
                throw new InvalidOperationException("Value is not centered: " + text.text);
            observations.Add("Centered value: " + text.text);
        }
        Capture("engine-settings.png");
        Invoke(game, "ShowSettingsSection", 2);
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        Capture("board-settings.png");
        File.WriteAllLines(Path.Combine(outputDirectory, "layout-check.txt"), observations);
        Debug.Log("UI VISUAL REVIEW OK");
        Application.Quit();
    }

    static void Invoke(GoGame game, string method, params object[] args)
    {
        typeof(GoGame).GetMethod(method, PrivateInstance).Invoke(game, args);
    }

    void Capture(string filename)
    {
        // Render the actual canvases offscreen so a minimized review window
        // cannot turn a successful capture into a black image.
        var cameraObject = new GameObject("ReviewCamera");
        var reviewCamera = cameraObject.AddComponent<Camera>();
        reviewCamera.enabled = false;
        reviewCamera.orthographic = true;
        reviewCamera.clearFlags = CameraClearFlags.SolidColor;
        reviewCamera.backgroundColor = Color.black;
        reviewCamera.transform.position = new Vector3(0f, 0f, -100f);
        var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
        reviewCamera.targetTexture = target;
        var canvases = FindObjectsOfType<Canvas>();
        var previousModes = new Dictionary<Canvas, RenderMode>();
        var previousCameras = new Dictionary<Canvas, Camera>();
        var previousDistances = new Dictionary<Canvas, float>();
        foreach (var canvas in canvases)
        {
            if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
            previousModes[canvas] = canvas.renderMode;
            previousCameras[canvas] = canvas.worldCamera;
            previousDistances[canvas] = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = reviewCamera;
            canvas.planeDistance = 10f;
        }
        Canvas.ForceUpdateCanvases();
        reviewCamera.Render();
        var previousTarget = RenderTexture.active;
        RenderTexture.active = target;
        var screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, filename), screenshot.EncodeToPNG());
        RenderTexture.active = previousTarget;
        foreach (var canvas in previousModes.Keys)
        {
            canvas.renderMode = previousModes[canvas];
            canvas.worldCamera = previousCameras[canvas];
            canvas.planeDistance = previousDistances[canvas];
        }
        reviewCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(target);
        Destroy(cameraObject);
        Destroy(screenshot);
    }
}
