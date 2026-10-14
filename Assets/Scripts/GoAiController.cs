using System;
using System.Collections;
using UnityEngine;

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
                int searchDone = 0;
                try
                {
                    GoAI.SearchAsync(rules, aiColor, budget, res =>
                    {
                        if (System.Threading.Interlocked.Exchange(ref searchDone, 1) == 0)
                            result = res ?? GoMoveResult.Fail("本地引擎未返回结果");
                    });
                }
                catch (Exception ex)
                {
                    result = GoMoveResult.Fail("本地引擎启动失败: " + ex.Message);
                    System.Threading.Interlocked.Exchange(ref searchDone, 1);
                }

                while (System.Threading.Thread.VolatileRead(ref searchDone) == 0)
                {
                    if (requestId != activeRequestId) yield break;
                    yield return null;
                }

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
