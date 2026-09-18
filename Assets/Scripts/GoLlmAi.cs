using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

// 通过 xAI / OpenAI 兼容接口请大模型选点。密钥不进版本库。
public static class GoLlmAi
{
    const string PrefKey = "GoLlmKey";
    const string PrefUrl = "GoLlmUrl";
    const string PrefModel = "GoLlmModel";
    const string DefaultUrl = "https://api.x.ai/v1/chat/completions";
    const string DefaultModel = "grok-4-fast";

    public static int ResultX, ResultY;
    public static bool ResultIsPass;
    public static string LastError;

    public static string ApiKey
    {
        get
        {
            string k = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(k)) return k.Trim();
            k = Environment.GetEnvironmentVariable("XAI_API_KEY");
            if (!string.IsNullOrEmpty(k)) return k.Trim();
            k = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(k)) return k.Trim();
            return ReadKeyFile();
        }
        set
        {
            PlayerPrefs.SetString(PrefKey, value ?? "");
            PlayerPrefs.Save();
        }
    }

    public static string ApiUrl
    {
        get
        {
            string u = PlayerPrefs.GetString(PrefUrl, "");
            if (!string.IsNullOrEmpty(u)) return u.Trim();
            u = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
            if (!string.IsNullOrEmpty(u))
            {
                u = u.Trim().TrimEnd('/');
                if (!u.EndsWith("/chat/completions")) u += "/chat/completions";
                return u;
            }
            return DefaultUrl;
        }
    }

    public static string Model
    {
        get
        {
            string m = PlayerPrefs.GetString(PrefModel, "");
            return string.IsNullOrEmpty(m) ? DefaultModel : m.Trim();
        }
    }

    public static bool IsConfigured() => !string.IsNullOrEmpty(ApiKey);

    static string ReadKeyFile()
    {
        string[] paths =
        {
            Path.Combine(Application.persistentDataPath, "llm_key.txt"),
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? "", "llm_key.txt")
        };
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    string t = File.ReadAllText(p).Trim();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch { }
        }
        return "";
    }

    public static IEnumerator RequestMove(GoRules rules, int color)
    {
        LastError = null;
        ResultIsPass = false;
        ResultX = ResultY = 0;
        string key = ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            LastError = "未配置密钥";
            yield break;
        }

        string user = BuildPrompt(rules, color, null);
        yield return PostChat(key, user);
        if (LastError != null) yield break;
        if (TryApply(rules, color, out _)) yield break;

        string bad = LastError ?? "非法坐标";
        LastError = null;
        user = BuildPrompt(rules, color, bad);
        yield return PostChat(key, user);
        if (LastError != null) yield break;
        TryApply(rules, color, out _);
    }

    static IEnumerator PostChat(string key, string user)
    {
        string body =
            "{\"model\":\"" + Esc(Model) + "\"," +
            "\"temperature\":0.2," +
            "\"max_tokens\":40," +
            "\"messages\":[" +
            "{\"role\":\"system\",\"content\":\"" + Esc(SystemPrompt) + "\"}," +
            "{\"role\":\"user\",\"content\":\"" + Esc(user) + "\"}]}";

        string json = null;
        string err = null;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers.Add("Authorization", "Bearer " + key);
                req.Timeout = 35000;
                req.ReadWriteTimeout = 35000;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var stream = req.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
            }
            catch (WebException we)
            {
                try
                {
                    if (we.Response != null)
                    {
                        using (var reader = new StreamReader(we.Response.GetResponseStream() ?? Stream.Null))
                            err = Trunc(reader.ReadToEnd(), 120);
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(err)) err = we.Message;
            }
            catch (Exception e)
            {
                err = e.Message;
            }
        });
        while (!task.IsCompleted) yield return null;
        if (!string.IsNullOrEmpty(err))
        {
            LastError = err;
            yield break;
        }
        string content = ExtractContent(json);
        if (string.IsNullOrEmpty(content))
        {
            LastError = "模型没有返回着法";
            yield break;
        }
        if (!ParseMove(content, out int x, out int y, out bool pass))
        {
            LastError = "无法解析着法: " + Trunc(content, 40);
            yield break;
        }
        ResultIsPass = pass;
        ResultX = x;
        ResultY = y;
    }

    static bool TryApply(GoRules rules, int color, out string why)
    {
        why = null;
        if (ResultIsPass) return true;
        if (!IsLegal(rules, ResultX, ResultY, color))
        {
            why = LastError = "非法着点 " + Coord(ResultX, ResultY);
            return false;
        }
        return true;
    }

    public static bool IsLegal(GoRules g, int x, int y, int color)
    {
        if (x < 0 || y < 0) return true;
        if (x >= g.Size || y >= g.Size) return false;
        if (!g.Simulate(g.Board, x, y, color, out int[] nb, out _)) return false;
        return !g.RepeatsPosition(nb);
    }

    const string SystemPrompt =
        "You are a strong human-like Go player. Reply with exactly one move: " +
        "GTP coordinate (A-H,J-T + rank, skipping letter I, rank 1 is the bottom) " +
        "or the word pass. No other text, no punctuation, no analysis.";

    static string BuildPrompt(GoRules rules, int color, string retry)
    {
        int n = rules.Size;
        var sb = new StringBuilder(n * n + 400);
        sb.Append(n).Append("路围棋，").Append(color == GoRules.BLACK ? "轮黑走" : "轮白走");
        sb.Append("，贴目").Append(rules.Komi.ToString("0.0")).Append("。X=黑 O=白 .=空。列 A-H J-T（无I），行号从底到顶。\n");
        if (!string.IsNullOrEmpty(retry))
            sb.Append("上一手非法：").Append(retry).Append("，请另选合法点。\n");
        sb.Append("  ");
        for (int x = 0; x < n; x++) { sb.Append(Col(x)); sb.Append(' '); }
        sb.Append('\n');
        for (int y = n - 1; y >= 0; y--)
        {
            sb.Append((y + 1).ToString().PadLeft(2));
            sb.Append(' ');
            for (int x = 0; x < n; x++)
            {
                int i = rules.Idx(x, y);
                char ch = rules.Board[i] == GoRules.BLACK ? 'X' : rules.Board[i] == GoRules.WHITE ? 'O' : '.';
                sb.Append(ch).Append(' ');
            }
            sb.Append('\n');
        }
        int nlog = rules.LoggedMoveCount;
        int from = Math.Max(0, nlog - 40);
        if (nlog > 0)
        {
            sb.Append("最近着法：");
            for (int i = from; i < nlog; i++)
            {
                if (!rules.GetLoggedMove(i, out int x, out int y, out bool pass)) continue;
                sb.Append(i + 1).Append('.');
                sb.Append(pass ? "pass" : Coord(x, y)).Append(' ');
            }
            sb.Append('\n');
        }
        sb.Append("请只回复一个着点。");
        return sb.ToString();
    }

    const string Letters = "ABCDEFGHJKLMNOPQRST";

    public static string Coord(int x, int y)
    {
        if (x < 0) return "pass";
        return Col(x) + (y + 1).ToString();
    }

    static string Col(int x) => x >= 0 && x < Letters.Length ? Letters[x].ToString() : "?";

    public static bool ParseMove(string raw, out int x, out int y, out bool pass)
    {
        x = y = 0; pass = false;
        if (string.IsNullOrEmpty(raw)) return false;
        string s = raw.Trim().ToUpperInvariant();
        s = s.Replace("．", ".").Replace("。", " ").Replace("，", " ").Replace(",", " ");
        if (s.Contains("PASS") || s.Contains("停一手") || s.Contains("PASS")) { pass = true; x = y = -1; return true; }
        if (s.Contains("认输") || s.Contains("RESIGN")) { pass = true; x = y = -1; return true; }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            int col = Letters.IndexOf(c);
            if (col < 0) continue;
            int j = i + 1;
            if (j >= s.Length || !char.IsDigit(s[j])) continue;
            int row = 0;
            while (j < s.Length && char.IsDigit(s[j])) { row = row * 10 + (s[j] - '0'); j++; }
            if (row <= 0) continue;
            x = col; y = row - 1; pass = false;
            return true;
        }
        return false;
    }

    static string ExtractContent(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var wrap = JsonUtility.FromJson<ChatWire>(json);
            if (wrap != null && wrap.choices != null && wrap.choices.Length > 0 && wrap.choices[0].message != null)
                return wrap.choices[0].message.content;
        }
        catch { }
        int i = json.IndexOf("\"content\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i = json.IndexOf(':', i);
        if (i < 0) return null;
        i = json.IndexOf('"', i);
        if (i < 0) return null;
        var sb = new StringBuilder();
        for (int k = i + 1; k < json.Length; k++)
        {
            char ch = json[k];
            if (ch == '\\' && k + 1 < json.Length)
            {
                char n = json[++k];
                sb.Append(n == 'n' ? '\n' : n);
            }
            else if (ch == '"') break;
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    [Serializable] class ChatWire { public ChatChoice[] choices; }
    [Serializable] class ChatChoice { public ChatMsg message; }
    [Serializable] class ChatMsg { public string content; }
}
