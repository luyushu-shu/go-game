using System;
using System.Collections;
using UnityEngine;

// Publishes an async AI result exactly once. The volatile published flag is
// written only after the result, so readers can never observe "done" first.
public sealed class GoAiCompletionLatch
{
    int claimed;
    int published;
    GoMoveResult result;

    public bool TryPublish(GoMoveResult value)
    {
        if (System.Threading.Interlocked.CompareExchange(ref claimed, 1, 0) != 0)
            return false;

        result = value ?? GoMoveResult.Fail("本地引擎未返回结果");
        System.Threading.Volatile.Write(ref published, 1);
        return true;
    }

    public bool TryGetResult(out GoMoveResult value)
    {
        if (System.Threading.Volatile.Read(ref published) == 0)
        {
            value = null;
            return false;
        }

        value = result;
        return true;
    }
}

// 统一调度 KataGo、LLM 与本地 MCTS AI，处理异常恢复、降级与并发保护
public class GoAiController : MonoBehaviour
{
    public bool IsBusy { get; private set; }
    int activeRequestId = 0;

    public void Cancel()
    {
        activeRequestId++;
        IsBusy = false;
        GoKataGo.CancelRequested = true;
    }

    public IEnumerator RequestAiMove(
        GoRules rules,
        int aiColor,
        int opponentChoice,
        int boardSize,
        int handicap,
        Func<int, int, int[]> handicapPts,
        Action<GoMoveResult, string> onComplete)
    {
        if (rules == null)
        {
            onComplete?.Invoke(GoMoveResult.Fail("规则引擎未初始化"), null);
            yield break;
        }

        int requestId = ++activeRequestId;
        IsBusy = true;
        GoKataGo.CancelRequested = false;

        GoMoveResult result = null;
        string fallbackNotice = null;
        bool completionSent = false;

        try
        {
            if (opponentChoice == 1) // KataGo
            {
                yield return GoKataGo.GenMoveAsync(rules, aiColor, handicap, handicapPts, res => result = res);
                if (requestId != activeRequestId) yield break;

                if (result == null || !result.success)
                {
                    string errMsg = result?.error ?? "KataGo 异常";
                    fallbackNotice = "KataGo 失败，改用本地引擎：" + errMsg;
                    result = null;
                }
            }
            else if (opponentChoice == 2 && GoLlmAi.IsConfigured()) // LLM
            {
                yield return GoLlmAi.RequestMoveAsync(rules, aiColor, res => result = res);
                if (requestId != activeRequestId) yield break;

                if (result == null || !result.success)
                {
                    string errMsg = result?.error ?? "大模型选点失败";
                    fallbackNotice = "大模型失败，改用本地引擎：" + errMsg;
                    result = null;
                }
            }

            // 如果选用本地 AI 或上述 AI 失败，降级使用本地 MCTS
            if (result == null)
            {
                float budget = boardSize <= 9 ? 2.0f : boardSize <= 13 ? 3.2f : 4.2f;
                var completion = new GoAiCompletionLatch();
                try
                {
                    GoAI.SearchAsync(rules, aiColor, budget, res => completion.TryPublish(res));
                }
                catch (Exception ex)
                {
                    completion.TryPublish(GoMoveResult.Fail("本地引擎启动失败: " + ex.Message));
                }

                GoMoveResult localResult;
                while (!completion.TryGetResult(out localResult))
                {
                    if (requestId != activeRequestId) yield break;
                    yield return null;
                }
                result = localResult;

                if (result != null && !result.isPass && result.winrate >= 0f && result.winrate < 0.18f && rules.MoveCount > 80)
                {
                    result = GoMoveResult.Resign();
                }
            }
        }
        finally
        {
            if (requestId == activeRequestId)
            {
                IsBusy = false;
                if (!completionSent)
                {
                    completionSent = true;
                    onComplete?.Invoke(result ?? GoMoveResult.Fail("AI 计算失败"), fallbackNotice);
                }
            }
        }
    }
}
