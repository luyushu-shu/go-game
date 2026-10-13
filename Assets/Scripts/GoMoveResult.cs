using System;

// 统一的 AI / 引擎决策结果，避免依赖静态全局状态
[Serializable]
public class GoMoveResult
{
    public bool success;
    public bool isPass;
    public bool isResign;
    public int x = -1;
    public int y = -1;
    public string error;
    public float winrate = -1f;
    public float scoreLead = 0f;

    public static GoMoveResult Move(int x, int y, float wr = -1f, float lead = 0f)
    {
        return new GoMoveResult
        {
            success = true,
            isPass = false,
            isResign = false,
            x = x,
            y = y,
            winrate = wr,
            scoreLead = lead,
            error = null
        };
    }

    public static GoMoveResult Pass()
    {
        return new GoMoveResult
        {
            success = true,
            isPass = true,
            isResign = false,
            x = -1,
            y = -1,
            error = null
        };
    }

    public static GoMoveResult Resign()
    {
        return new GoMoveResult
        {
            success = true,
            isPass = false,
            isResign = true,
            x = -1,
            y = -1,
            error = null
        };
    }

    public static GoMoveResult Fail(string err)
    {
        return new GoMoveResult
        {
            success = false,
            isPass = false,
            isResign = false,
            x = -1,
            y = -1,
            error = string.IsNullOrEmpty(err) ? "未知错误" : err
        };
    }
}
