using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

// KataGo 局面分析候选选点与变化图
[Serializable]
public class KataCandidate
{
    public int x = -1, y = -1;
    public string coord = "";
    public int visits = 0;
    public float winrate = 0.5f;       // 当前落子方胜率
    public float blackWinrate = 0.5f;  // 黑方胜率
    public float scoreLead = 0f;       // 当前落子方领先目数
    public float blackScoreLead = 0f;  // 黑方领先目数
    public int order = 0;              // 推荐位次 (0 为最佳)
    public List<string> pv = new List<string>(); // 推荐后续走子分支 (PV)
}

[Serializable]
public class KataPositionAnalysis
{
    public int turn = GoRules.BLACK;
    public float blackWinrate = 0.5f;
    public float blackScoreLead = 0f;
    public List<KataCandidate> candidates = new List<KataCandidate>();
}

[Serializable]
public class KataGameAnalysis
{
    public int totalMoves;
    public float[] blackWinrates;
    public float[] blackScoreLeads;
    public int[] blunders; // 0=正常, 1=疑问手(失误), 2=恶手

    public string ToJson() => JsonUtility.ToJson(this);
    public static KataGameAnalysis FromJson(string json) =>
        string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<KataGameAnalysis>(json);
}

// 通过 GTP 调用本机 KataGo（Tools/KataGo）。
public static class GoKataGo
{
    public static int ResultX, ResultY;
    public static bool ResultIsPass, ResultIsResign;
    public static string LastError;
    public static volatile bool CancelRequested = false;

    static Process proc;
    static readonly object gate = new object();
    static string exePath, modelPath, cfgPath;

    public const string Letters = "ABCDEFGHJKLMNOPQRST";

    public static bool IsAvailable()
    {
        ResolvePaths();
        return File.Exists(exePath) && File.Exists(modelPath) && File.Exists(cfgPath);
    }

    public static string InstallHint()
    {
        ResolvePaths();
        return "请把 katago.exe 与 default_model.bin.gz 放到：\n" + Dir();
    }

    static string Dir()
    {
        string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(root, "Tools", "KataGo");
    }

    static void ResolvePaths()
    {
        string d = Dir();
        cfgPath = Path.Combine(d, "gtp.cfg");
        modelPath = Path.Combine(d, "default_model.bin.gz");
        string opencl = Path.Combine(d, "katago.exe");
        string eigen = Path.Combine(d, "katago-eigen.exe");
        exePath = File.Exists(opencl) ? opencl : eigen;
    }

    public static void Shutdown()
    {
        lock (gate)
        {
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    try { proc.StandardInput.WriteLine("quit"); proc.StandardInput.Flush(); }
                    catch { }
                    if (!proc.WaitForExit(800)) proc.Kill();
                }
            }
            catch { }
            proc = null;
        }
    }

    public static IEnumerator GenMove(GoRules rules, int color, int handicap, Func<int, int, int[]> handicapPts)
    {
        GoMoveResult res = null;
        yield return GenMoveAsync(rules, color, handicap, handicapPts, r => res = r);
        if (res != null && res.success)
        {
            LastError = null;
            ResultIsPass = res.isPass;
            ResultIsResign = res.isResign;
            ResultX = res.x;
            ResultY = res.y;
        }
        else
        {
            LastError = res?.error ?? "KataGo 失败";
            ResultIsPass = ResultIsResign = false;
            ResultX = ResultY = -1;
        }
    }

    public static IEnumerator GenMoveAsync(GoRules rules, int color, int handicap, Func<int, int, int[]> handicapPts, Action<GoMoveResult> onComplete)
    {
        if (!IsAvailable())
        {
            onComplete?.Invoke(GoMoveResult.Fail("未找到 KataGo"));
            yield break;
        }

        string cmdBoard =
            "boardsize " + rules.Size + "\n" +
            "clear_board\n" +
            "komi " + rules.Komi.ToString("0.0", CultureInfo.InvariantCulture) + "\n" +
            "kata-set-rules chinese\n";
        if (handicap > 0 && handicapPts != null)
        {
            int[] pts = handicapPts(rules.Size, handicap);
            for (int i = 0; i < pts.Length; i++)
            {
                int p = pts[i];
                cmdBoard += "play B " + Coord(p % rules.Size, p / rules.Size) + "\n";
            }
        }
        int nlog = rules.LoggedMoveCount;
        for (int i = 0; i < nlog; i++)
        {
            if (!rules.GetLoggedMove(i, out int x, out int y, out int c, out bool loggedPass)) continue;
            string who = c == GoRules.BLACK ? "B" : "W";
            cmdBoard += "play " + who + " " + (loggedPass ? "pass" : Coord(x, y)) + "\n";
        }
        string colorGtp = color == GoRules.BLACK ? "B" : "W";
        cmdBoard += "genmove " + colorGtp;

        string reply = null;
        string err = null;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!EnsureProcess())
                {
                    err = LastError ?? "KataGo 未能启动";
                    return;
                }
                reply = SendBlock(cmdBoard);
            }
            catch (Exception e)
            {
                err = e.Message;
            }
        });
        while (!task.IsCompleted) yield return null;
        if (!string.IsNullOrEmpty(err))
        {
            onComplete?.Invoke(GoMoveResult.Fail(err));
            yield break;
        }
        if (string.IsNullOrEmpty(reply))
        {
            onComplete?.Invoke(GoMoveResult.Fail("KataGo 无响应"));
            yield break;
        }
        if (!ParseGenmove(reply, out int gx, out int gy, out bool pass, out bool resign))
        {
            onComplete?.Invoke(GoMoveResult.Fail("KataGo 返回无法解析: " + Trunc(reply, 80)));
            yield break;
        }

        if (resign) onComplete?.Invoke(GoMoveResult.Resign());
        else if (pass) onComplete?.Invoke(GoMoveResult.Pass());
        else onComplete?.Invoke(GoMoveResult.Move(gx, gy));
    }

    static bool EnsureProcess()
    {
        lock (gate)
        {
            if (proc != null)
            {
                if (!proc.HasExited) return true;
                try { proc.Dispose(); } catch { }
                proc = null;
            }
            ResolvePaths();
            try
            {
                proc = Start(exePath);
                if (proc.HasExited)
                {
                    string eigen = Path.Combine(Dir(), "katago-eigen.exe");
                    if (File.Exists(eigen) && eigen != exePath)
                    {
                        try { proc.Dispose(); } catch { }
                        proc = Start(eigen);
                    }
                }
                if (proc == null || proc.HasExited)
                {
                    LastError = "KataGo 未能启动";
                    proc = null;
                    return false;
                }
                SendBlock("protocol_version");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                proc = null;
                return false;
            }
        }
    }

    static Process Start(string exe)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "gtp -model \"" + modelPath + "\" -config \"" + cfgPath + "\"",
            WorkingDirectory = Dir(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        var p = Process.Start(psi);
        p.ErrorDataReceived += (_, ev) => { };
        p.BeginErrorReadLine();
        return p;
    }

    static string SendBlock(string commands)
    {
        lock (gate)
        {
            if (proc == null || proc.HasExited)
            {
                if (!EnsureProcess()) throw new Exception(LastError ?? "KataGo 进程未运行");
            }
            var parts = commands.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string last = "";
            foreach (var raw in parts)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    proc.StandardInput.WriteLine(line);
                    proc.StandardInput.Flush();
                    last = ReadUntilBlank(120000);
                }
                catch (Exception ex)
                {
                    try { proc?.Kill(); } catch { }
                    proc = null;
                    throw new Exception("KataGo 通信失败: " + ex.Message);
                }
            }
            return last;
        }
    }

    static string ReadUntilBlank(int timeoutMs)
    {
        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (proc == null || proc.HasExited) break;
            string line = proc.StandardOutput.ReadLine();
            if (line == null) break;
            if (line.Length == 0)
            {
                if (sb.Length > 0) return sb.ToString();
                continue;
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }

    static bool ParseGenmove(string reply, out int x, out int y, out bool pass, out bool resign)
    {
        x = y = 0; pass = resign = false;
        if (string.IsNullOrEmpty(reply)) return false;
        string s = reply.Trim();
        if (s.StartsWith("?")) return false;
        if (s.StartsWith("=")) s = s.Substring(1).Trim();
        string u = s.ToUpperInvariant();
        if (u == "PASS") { pass = true; x = y = -1; return true; }
        if (u == "RESIGN") { resign = true; x = y = -1; return true; }
        if (u.Length < 2) return false;
        int col = Letters.IndexOf(u[0]);
        if (col < 0) return false;
        if (!int.TryParse(u.Substring(1), out int row) || row <= 0) return false;
        x = col; y = row - 1;
        return true;
    }

    public static string Coord(int x, int y)
    {
        if (x < 0) return "pass";
        return Letters[x] + (y + 1).ToString();
    }

    public static void CancelAnalysis()
    {
        CancelRequested = true;
    }

    public static IEnumerator AnalyzePosition(
        GoRules rules,
        int colorToMove,
        int handicap,
        Func<int, int, int[]> handicapPts,
        int maxVisits,
        Action<KataPositionAnalysis> onComplete)
    {
        CancelRequested = false;
        LastError = null;
        if (!IsAvailable())
        {
            LastError = "未找到 KataGo";
            onComplete?.Invoke(null);
            yield break;
        }

        string cmdBoard =
            "boardsize " + rules.Size + "\n" +
            "clear_board\n" +
            "komi " + rules.Komi.ToString("0.0", CultureInfo.InvariantCulture) + "\n" +
            "kata-set-rules chinese\n";
        if (handicap > 0 && handicapPts != null)
        {
            int[] pts = handicapPts(rules.Size, handicap);
            for (int i = 0; i < pts.Length; i++)
            {
                int p = pts[i];
                cmdBoard += "play B " + Coord(p % rules.Size, p / rules.Size) + "\n";
            }
        }
        int nlog = rules.LoggedMoveCount;
        for (int i = 0; i < nlog; i++)
        {
            if (!rules.GetLoggedMove(i, out int x, out int y, out int c, out bool loggedPass)) continue;
            string who = c == GoRules.BLACK ? "B" : "W";
            cmdBoard += "play " + who + " " + (loggedPass ? "pass" : Coord(x, y)) + "\n";
        }
        string colorGtp = colorToMove == GoRules.BLACK ? "B" : "W";
        cmdBoard += "kata-set-param maxVisits " + maxVisits + "\n";
        cmdBoard += "kata-search_analyze " + colorGtp;

        string reply = null;
        string err = null;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!EnsureProcess()) return;
                reply = SendBlock(cmdBoard);
            }
            catch (Exception e) { err = e.Message; }
        });
        while (!task.IsCompleted) yield return null;
        if (!string.IsNullOrEmpty(err))
        {
            LastError = err;
            onComplete?.Invoke(null);
            yield break;
        }
        var analysis = ParseAnalysis(reply, colorToMove);
        onComplete?.Invoke(analysis);
    }

    struct MoveInfo
    {
        public int x, y, c;
    }

    public static IEnumerator AnalyzeFullGame(
        int boardSize,
        float komi,
        int handicap,
        Func<int, int, int[]> handicapPts,
        string moveLogCsv,
        int totalMoves,
        int maxVisitsPerMove,
        Action<int, int, float, float> onProgress,
        Action<KataGameAnalysis> onComplete)
    {
        CancelRequested = false;
        LastError = null;
        if (!IsAvailable())
        {
            LastError = "未找到 KataGo";
            onComplete?.Invoke(null);
            yield break;
        }

        var moves = new List<MoveInfo>();
        if (!string.IsNullOrEmpty(moveLogCsv))
        {
            foreach (string tok in moveLogCsv.Split('|'))
            {
                if (string.IsNullOrEmpty(tok)) continue;
                var p = tok.Split(',');
                if (p.Length < 3) continue;
                if (int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y) && int.TryParse(p[2], out int c))
                    moves.Add(new MoveInfo { x = x, y = y, c = c });
            }
        }
        if (totalMoves > moves.Count) totalMoves = moves.Count;

        var result = new KataGameAnalysis
        {
            totalMoves = totalMoves,
            blackWinrates = new float[totalMoves + 1],
            blackScoreLeads = new float[totalMoves + 1],
            blunders = new int[totalMoves + 1]
        };

        int currentProgressPly = -1;
        float currentProgressWr = 0.5f;
        float currentProgressLead = 0f;
        string err = null;
        bool isDone = false;

        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!EnsureProcess()) return;

                string initCmd =
                    "boardsize " + boardSize + "\n" +
                    "clear_board\n" +
                    "komi " + komi.ToString("0.0", CultureInfo.InvariantCulture) + "\n" +
                    "kata-set-rules chinese\n" +
                    "kata-set-param maxVisits " + maxVisitsPerMove + "\n";
                if (handicap > 0 && handicapPts != null)
                {
                    int[] pts = handicapPts(boardSize, handicap);
                    for (int i = 0; i < pts.Length; i++)
                        initCmd += "play B " + Coord(pts[i] % boardSize, pts[i] / boardSize) + "\n";
                }
                SendBlock(initCmd);

                for (int ply = 0; ply <= totalMoves; ply++)
                {
                    if (CancelRequested) break;

                    int sideToMove;
                    if (ply < moves.Count)
                    {
                        sideToMove = moves[ply].c;
                    }
                    else
                    {
                        sideToMove = moves.Count > 0
                            ? GoRules.Opp(moves[moves.Count - 1].c)
                            : (handicap > 0 ? GoRules.WHITE : GoRules.BLACK);
                    }

                    string searchCmd = "kata-search_analyze " + (sideToMove == GoRules.BLACK ? "B" : "W");
                    string reply = SendBlock(searchCmd);
                    var pos = ParseAnalysis(reply, sideToMove);

                    float wr = pos != null ? pos.blackWinrate : 0.5f;
                    float lead = pos != null ? pos.blackScoreLead : 0f;
                    result.blackWinrates[ply] = wr;
                    result.blackScoreLeads[ply] = lead;

                    currentProgressPly = ply;
                    currentProgressWr = wr;
                    currentProgressLead = lead;

                    if (ply < totalMoves)
                    {
                        var m = moves[ply];
                        string who = m.c == GoRules.BLACK ? "B" : "W";
                        string coord = m.x < 0 ? "pass" : Coord(m.x, m.y);
                        SendBlock("play " + who + " " + coord);
                    }
                }

                // 计算失误与恶手标记
                for (int i = 1; i <= totalMoves; i++)
                {
                    int mover = moves[i - 1].c;
                    float wrBefore = mover == GoRules.BLACK ? result.blackWinrates[i - 1] : 1.0f - result.blackWinrates[i - 1];
                    float wrAfter = mover == GoRules.BLACK ? result.blackWinrates[i] : 1.0f - result.blackWinrates[i];
                    float drop = wrBefore - wrAfter;
                    if (drop >= 0.18f) result.blunders[i] = 2; // 恶手
                    else if (drop >= 0.08f) result.blunders[i] = 1; // 疑问手
                }
            }
            catch (Exception e) { err = e.Message; }
            finally { isDone = true; }
        });

        int lastReported = -1;
        while (!isDone)
        {
            if (currentProgressPly > lastReported)
            {
                lastReported = currentProgressPly;
                onProgress?.Invoke(lastReported, totalMoves, currentProgressWr, currentProgressLead);
            }
            yield return null;
        }

        if (!string.IsNullOrEmpty(err))
        {
            LastError = err;
            onComplete?.Invoke(null);
            yield break;
        }
        if (CancelRequested)
        {
            LastError = "分析已取消";
            onComplete?.Invoke(null);
            yield break;
        }

        onComplete?.Invoke(result);
    }

    public static KataPositionAnalysis ParseAnalysis(string reply, int sideToMove)
    {
        if (string.IsNullOrEmpty(reply)) return null;
        var pos = new KataPositionAnalysis();
        pos.turn = sideToMove;

        int markerIdx = reply.IndexOf("info move ", StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return null;

        string sub = reply.Substring(markerIdx);
        string[] chunks = sub.Split(new[] { "info move ", "info move\t" }, StringSplitOptions.RemoveEmptyEntries);

        for (int c = 0; c < chunks.Length; c++)
        {
            string chunk = chunks[c].Trim();
            if (chunk.Length == 0) continue;
            var tokens = chunk.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var cand = new KataCandidate();
            cand.coord = tokens[0];
            ParseCoord(cand.coord, out cand.x, out cand.y);

            bool inPv = false;
            for (int i = 1; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok == "pv")
                {
                    inPv = true;
                    continue;
                }
                if (inPv)
                {
                    if (IsCoord(tok))
                    {
                        cand.pv.Add(tok);
                        continue;
                    }
                    else
                    {
                        inPv = false;
                    }
                }

                if (i + 1 < tokens.Length)
                {
                    if (tok == "visits" && int.TryParse(tokens[i + 1], out int v)) { cand.visits = v; i++; }
                    else if (tok == "winrate" && float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float wr)) { cand.winrate = wr; i++; }
                    else if (tok == "scoreLead" && float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sl)) { cand.scoreLead = sl; i++; }
                    else if (tok == "order" && int.TryParse(tokens[i + 1], out int ord)) { cand.order = ord; i++; }
                }
            }

            if (sideToMove == GoRules.BLACK)
            {
                cand.blackWinrate = cand.winrate;
                cand.blackScoreLead = cand.scoreLead;
            }
            else
            {
                cand.blackWinrate = 1.0f - cand.winrate;
                cand.blackScoreLead = -cand.scoreLead;
            }

            pos.candidates.Add(cand);
        }

        pos.candidates.Sort((a, b) => a.order.CompareTo(b.order));
        if (pos.candidates.Count > 0)
        {
            pos.blackWinrate = pos.candidates[0].blackWinrate;
            pos.blackScoreLead = pos.candidates[0].blackScoreLead;
        }

        return pos;
    }

    public static bool ParseCoord(string s, out int x, out int y)
    {
        x = y = -1;
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Equals("pass", StringComparison.OrdinalIgnoreCase)) return true;
        char colC = char.ToUpperInvariant(s[0]);
        int col = Letters.IndexOf(colC);
        if (col < 0) return false;
        if (!int.TryParse(s.Substring(1), out int row) || row <= 0) return false;
        x = col;
        y = row - 1;
        return true;
    }

    public static bool IsCoord(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Equals("pass", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Length < 2 || s.Length > 3) return false;
        char colC = char.ToUpperInvariant(s[0]);
        if (Letters.IndexOf(colC) < 0) return false;
        return int.TryParse(s.Substring(1), out int row) && row >= 1 && row <= 25;
    }

    static string Trunc(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n);
    }
}
