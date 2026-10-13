using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime presentation for one player's clock. The controller remains the
/// source of truth; this component only formats snapshots and presents the
/// warning state.
/// </summary>
public sealed class GoClockView : MonoBehaviour
{
    const double WarningSeconds = 10.0;
    static readonly Color NormalColor = new Color(0.949f, 0.910f, 0.820f, 1f);
    static readonly Color WarningColor = new Color(0.714f, 0.290f, 0.227f, 1f);

    Text timeText;
    Text stateText;
    GoClockController controller;
    int color;
    int totalByoyomiPeriods;
    int warnedSegment = -1;
    int warningCount;
    bool warning;

    public string DisplayText => timeText != null ? timeText.text : string.Empty;
    public bool IsWarning => warning;
    public int WarningCount => warningCount;

    public void Bind(GoClockController value, int playerColor)
    {
        if (controller != null)
            controller.Changed -= OnClockChanged;

        controller = value;
        color = playerColor;
        totalByoyomiPeriods = controller != null ? controller.ByoyomiPeriodsTotal : 0;
        warnedSegment = -1;
        warningCount = 0;
        warning = false;

        if (controller != null)
        {
            controller.Changed += OnClockChanged;
            Render(controller.GetSnapshot(color), controller.IsRunning && controller.ActiveColor == color);
        }
    }

    public void Render(GoClockSnapshot snapshot, bool active)
    {
        EnsureText();

        if (snapshot.InByoyomi)
        {
            int total = totalByoyomiPeriods > 0
                ? totalByoyomiPeriods
                : Math.Max(snapshot.ByoyomiPeriodsLeft, 1);
            int current = Math.Max(1, snapshot.ByoyomiPeriodsLeft);
            timeText.text = $"读秒 {current}/{total} · {FormatByoyomiSeconds(snapshot.ByoyomiSeconds)}";
            stateText.text = active ? "行棋中" : "等待行棋";

            bool nextWarning = !snapshot.TimedOut && snapshot.ByoyomiSeconds > 0.0
                && snapshot.ByoyomiSeconds <= WarningSeconds;
            if (nextWarning && warnedSegment != snapshot.ByoyomiSegment)
            {
                warnedSegment = snapshot.ByoyomiSegment;
                warningCount++;
                GoAudioManager.Instance?.PlaySfx("clock_warning");
            }
            SetWarning(nextWarning);
        }
        else
        {
            timeText.text = double.IsPositiveInfinity(snapshot.MainSeconds)
                ? "∞"
                : FormatSeconds(snapshot.MainSeconds);
            stateText.text = active ? "本时" : "等待行棋";
            SetWarning(false);
        }

        if (snapshot.TimedOut)
        {
            timeText.text = "0:00";
            stateText.text = "超时";
        }
    }

    public void SetWarning(bool value)
    {
        warning = value;
        EnsureText();
        timeText.color = value ? WarningColor : NormalColor;
        stateText.color = value ? WarningColor : new Color(0.725f, 0.667f, 0.549f, 1f);
    }

    void OnClockChanged(GoClockSnapshot before, GoClockSnapshot after)
    {
        if (after.Color != color) return;
        Render(after, controller != null && controller.IsRunning && controller.ActiveColor == color);
    }

    void EnsureText()
    {
        if (timeText != null && stateText != null) return;

        var root = transform;
        var layout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 54f;
        layout.minHeight = 54f;
        layout.flexibleWidth = 1f;

        if (timeText == null)
            timeText = CreateText(root, "Time", 22, NormalColor, TextAnchor.MiddleCenter);
        if (stateText == null)
            stateText = CreateText(root, "State", 11, new Color(0.725f, 0.667f, 0.549f, 1f), TextAnchor.LowerCenter);

        var group = GetComponent<VerticalLayoutGroup>();
        if (group == null)
        {
            group = gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = -2f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
        }
    }

    static Text CreateText(Transform parent, string objectName, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, size);
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = size + 8f;
        layout.minHeight = size + 8f;
        return text;
    }

    public static string FormatSeconds(double seconds)
    {
        if (double.IsPositiveInfinity(seconds)) return "∞";
        int whole = Math.Max(0, (int)Math.Ceiling(seconds - 0.000001));
        int hours = whole / 3600;
        int minutes = (whole % 3600) / 60;
        int remaining = whole % 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{remaining:00}"
            : $"{whole / 60}:{remaining:00}";
    }

    static string FormatByoyomiSeconds(double seconds)
    {
        int whole = Math.Max(0, (int)Math.Ceiling(seconds - 0.000001));
        return $"{whole / 60:00}:{whole % 60:00}";
    }

    void OnDestroy()
    {
        if (controller != null)
            controller.Changed -= OnClockChanged;
    }
}
