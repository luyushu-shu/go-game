using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

// 通过 GTP 调用本机 KataGo（Tools/KataGo）。
public static class GoKataGo
{
    public static int ResultX, ResultY;
    public static bool ResultIsPass, ResultIsResign;
    public static string LastError;

    static Process proc;
    static readonly object gate = new object();
    static string exePath, modelPath, cfgPath;

    const string Letters = "ABCDEFGHJKLMNOPQRST";

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
        LastError = null;
        ResultIsPass = ResultIsResign = false;
        ResultX = ResultY = 0;
        if (!IsAvailable())
        {
            LastError = "未找到 KataGo";
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
            if (!rules.GetLoggedMove(i, out int x, out int y, out bool loggedPass)) continue;
            string who = handicap > 0
                ? ((i % 2 == 0) ? "W" : "B")
                : ((i % 2 == 0) ? "B" : "W");
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
                if (!EnsureProcess()) return;
                reply = SendBlock(cmdBoard);
            }
            catch (Exception e) { err = e.Message; }
        });
        while (!task.IsCompleted) yield return null;
        if (!string.IsNullOrEmpty(err))
        {
            LastError = err;
            yield break;
        }
        if (string.IsNullOrEmpty(reply))
        {
            if (string.IsNullOrEmpty(LastError)) LastError = "KataGo 无响应";
            yield break;
        }
        if (!ParseGenmove(reply, out int gx, out int gy, out bool pass, out bool resign))
        {
            LastError = "KataGo 返回无法解析: " + Trunc(reply, 80);
            yield break;
        }
        ResultIsPass = pass;
        ResultIsResign = resign;
        ResultX = gx;
        ResultY = gy;
    }

    static bool EnsureProcess()
    {
        lock (gate)
        {
            if (proc != null && !proc.HasExited) return true;
            ResolvePaths();
            try
            {
                proc = Start(exePath);
                if (proc.HasExited)
                {
                    string eigen = Path.Combine(Dir(), "katago-eigen.exe");
                    if (File.Exists(eigen) && eigen != exePath)
                    {
                        proc = Start(eigen);
                    }
                }
                if (proc == null || proc.HasExited)
                {
                    LastError = "KataGo 未能启动";
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
            var parts = commands.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string last = "";
            foreach (var raw in parts)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                proc.StandardInput.WriteLine(line);
                proc.StandardInput.Flush();
                last = ReadUntilBlank(120000);
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
            if (proc.HasExited) break;
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

    static string Trunc(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n);
    }
}
