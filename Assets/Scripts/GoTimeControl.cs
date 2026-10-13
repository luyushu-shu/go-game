using System;
using System.Globalization;

public enum GoTimePreset { Unlimited, FiveMinutes, OneHour, TwoHours, Custom }
public enum GoByoyomiPreset { None, ThreeByThirty, FiveByThirty, FiveBySixty, Custom }

[Serializable]
public struct GoTimeControl
{
    public GoTimePreset MainPreset;
    public GoByoyomiPreset ByoyomiPreset;
    public int MainTimeSeconds;
    public int ByoyomiPeriods;
    public int ByoyomiSeconds;

    public bool IsUnlimited { get { return MainTimeSeconds == 0; } }
    public bool HasByoyomi { get { return ByoyomiPeriods > 0 && ByoyomiSeconds > 0; } }

    public static GoTimeControl FromPreset(GoTimePreset main, GoByoyomiPreset byoyomi)
    {
        var control = new GoTimeControl
        {
            MainPreset = main,
            ByoyomiPreset = byoyomi
        };

        switch (main)
        {
            case GoTimePreset.Unlimited:
                control.MainTimeSeconds = 0;
                break;
            case GoTimePreset.FiveMinutes:
                control.MainTimeSeconds = 5 * 60;
                break;
            case GoTimePreset.OneHour:
                control.MainTimeSeconds = 60 * 60;
                break;
            case GoTimePreset.TwoHours:
                control.MainTimeSeconds = 2 * 60 * 60;
                break;
            case GoTimePreset.Custom:
                control.MainTimeSeconds = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(main), main, "Unknown main time preset.");
        }

        switch (byoyomi)
        {
            case GoByoyomiPreset.None:
                control.ByoyomiPeriods = 0;
                control.ByoyomiSeconds = 0;
                break;
            case GoByoyomiPreset.ThreeByThirty:
                control.ByoyomiPeriods = 3;
                control.ByoyomiSeconds = 30;
                break;
            case GoByoyomiPreset.FiveByThirty:
                control.ByoyomiPeriods = 5;
                control.ByoyomiSeconds = 30;
                break;
            case GoByoyomiPreset.FiveBySixty:
                control.ByoyomiPeriods = 5;
                control.ByoyomiSeconds = 60;
                break;
            case GoByoyomiPreset.Custom:
                control.ByoyomiPeriods = 0;
                control.ByoyomiSeconds = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(byoyomi), byoyomi, "Unknown byoyomi preset.");
        }

        return control;
    }

    public bool TryValidate(out string error)
    {
        error = null;

        switch (MainPreset)
        {
            case GoTimePreset.Unlimited:
                if (MainTimeSeconds != 0)
                {
                    error = "无限制本时必须为 0 秒。";
                    return false;
                }
                break;
            case GoTimePreset.FiveMinutes:
                if (MainTimeSeconds != 5 * 60)
                {
                    error = "5 分钟本时必须为 300 秒。";
                    return false;
                }
                break;
            case GoTimePreset.OneHour:
                if (MainTimeSeconds != 60 * 60)
                {
                    error = "1 小时本时必须为 3600 秒。";
                    return false;
                }
                break;
            case GoTimePreset.TwoHours:
                if (MainTimeSeconds != 2 * 60 * 60)
                {
                    error = "2 小时本时必须为 7200 秒。";
                    return false;
                }
                break;
            case GoTimePreset.Custom:
                if (MainTimeSeconds < 60 || MainTimeSeconds > 600 * 60)
                {
                    error = "自定义本时必须为 1–600 分钟。";
                    return false;
                }
                break;
            default:
                error = "未知的本时预设。";
                return false;
        }

        switch (ByoyomiPreset)
        {
            case GoByoyomiPreset.None:
                if (ByoyomiPeriods != 0 || ByoyomiSeconds != 0)
                {
                    error = "无读秒必须为 0 段、0 秒。";
                    return false;
                }
                break;
            case GoByoyomiPreset.ThreeByThirty:
                if (ByoyomiPeriods != 3 || ByoyomiSeconds != 30)
                {
                    error = "3×30 秒读秒必须为 3 段、每段 30 秒。";
                    return false;
                }
                break;
            case GoByoyomiPreset.FiveByThirty:
                if (ByoyomiPeriods != 5 || ByoyomiSeconds != 30)
                {
                    error = "5×30 秒读秒必须为 5 段、每段 30 秒。";
                    return false;
                }
                break;
            case GoByoyomiPreset.FiveBySixty:
                if (ByoyomiPeriods != 5 || ByoyomiSeconds != 60)
                {
                    error = "5×60 秒读秒必须为 5 段、每段 60 秒。";
                    return false;
                }
                break;
            case GoByoyomiPreset.Custom:
                if (ByoyomiPeriods < 1 || ByoyomiPeriods > 20 ||
                    ByoyomiSeconds < 5 || ByoyomiSeconds > 600)
                {
                    error = "自定义读秒必须为 1–20 段、每段 5–600 秒。";
                    return false;
                }
                break;
            default:
                error = "未知的读秒预设。";
                return false;
        }

        return true;
    }

    public string MainLabel()
    {
        switch (MainPreset)
        {
            case GoTimePreset.Unlimited:
                return "无限制";
            case GoTimePreset.FiveMinutes:
                return "5分钟";
            case GoTimePreset.OneHour:
                return "1小时";
            case GoTimePreset.TwoHours:
                return "2小时";
            case GoTimePreset.Custom:
                if (MainTimeSeconds >= 60 && MainTimeSeconds % 60 == 0)
                    return (MainTimeSeconds / 60).ToString(CultureInfo.InvariantCulture) + "分钟";
                return MainTimeSeconds.ToString(CultureInfo.InvariantCulture) + "秒";
            default:
                return "自定义";
        }
    }

    public string ByoyomiLabel()
    {
        switch (ByoyomiPreset)
        {
            case GoByoyomiPreset.None:
                return "无读秒";
            case GoByoyomiPreset.ThreeByThirty:
                return "3×30秒";
            case GoByoyomiPreset.FiveByThirty:
                return "5×30秒";
            case GoByoyomiPreset.FiveBySixty:
                return "5×60秒";
            case GoByoyomiPreset.Custom:
                return ByoyomiPeriods.ToString(CultureInfo.InvariantCulture) + "×" +
                    ByoyomiSeconds.ToString(CultureInfo.InvariantCulture) + "秒";
            default:
                return "自定义读秒";
        }
    }
}
