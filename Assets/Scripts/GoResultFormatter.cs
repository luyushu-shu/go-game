using System.Globalization;

public static class GoResultFormatter
{
    public static string FormatScore(float blackScore, float whiteScore)
    {
        float difference = System.Math.Abs(blackScore - whiteScore);
        string winner = blackScore > whiteScore ? "黑方胜 " : "白方胜 ";
        return winner + difference.ToString("0.0", CultureInfo.InvariantCulture) + " 目";
    }

    public static string FormatResign(int resignColor)
    {
        return resignColor == GoRules.BLACK ? "白方中盘胜" : "黑方中盘胜";
    }

    public static string FormatTimeout(int loserColor)
    {
        return loserColor == GoRules.BLACK ? "白方胜（黑方超时负）" : "黑方胜（白方超时负）";
    }
}
