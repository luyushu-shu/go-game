using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public class KifuRecord
{
    public int schemaVersion = 1;
    public string id;
    public string title;
    public string blackName;
    public string whiteName;
    public string blackRank;
    public string whiteRank;
    public string description;
    public string playedAt;
    public string result;
    public int boardSize;
    public string mode;
    public int moveCount;
    public float komi;
    public int handicap;
    public string moveLogCsv;
    public string stateJson;
    public string analysisJson;
    public string commentsJson;
    public string branchesJson;
}

[Serializable]
public class KifuCatalog
{
    public int schemaVersion = 1;
    public List<KifuRecord> items = new List<KifuRecord>();
}

public enum KifuSort
{
    PlayedAtDescending,
    PlayedAtAscending,
    MoveCountDescending,
    Result
}

[Serializable]
public class KifuQuery
{
    public string Keyword;
    public string Mode;
    public int BoardSize;
    public KifuSort Sort = KifuSort.PlayedAtDescending;
}

// 本地 JSON 棋谱库：写入 persistentDataPath，支持按关键字查询与原子安全写入。
public static class KifuDatabase
{
    public static string FilePath => Path.Combine(Application.persistentDataPath, "kifu_db.json");
    public static string LastError { get; private set; }

    static KifuCatalog cache;

    public static bool IsSupportedBoardSize(int size)
    {
        return size == 9 || size == 13 || size == 19;
    }

    public static IReadOnlyList<KifuRecord> All()
    {
        EnsureLoaded();
        return cache.items;
    }

    public static List<KifuRecord> Query(string keyword)
    {
        return Query(new KifuQuery { Keyword = keyword });
    }

    public static List<KifuRecord> Query(KifuQuery query)
    {
        EnsureLoaded();
        query = query ?? new KifuQuery();
        string keyword = (query.Keyword ?? "").Trim();
        string mode = (query.Mode ?? "").Trim();
        var list = new List<KifuRecord>();
        for (int i = 0; i < cache.items.Count; i++)
        {
            var record = cache.items[i];
            if (record == null || !Matches(record, keyword)) continue;
            if (query.BoardSize > 0 && record.boardSize != query.BoardSize) continue;
            if (!string.IsNullOrEmpty(mode) && !string.Equals(record.mode, mode, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(record);
        }

        list.Sort((a, b) => CompareRecords(a, b, query.Sort));
        return list;
    }

    public static bool ValidateRecord(KifuRecord rec, out string error)
    {
        error = null;
        if (rec == null)
        {
            error = "棋谱记录不能为空";
            return false;
        }
        if (!IsSupportedBoardSize(rec.boardSize))
        {
            error = $"不支持的棋盘路数: {rec.boardSize}";
            return false;
        }
        return true;
    }

    public static void MigrateRecord(KifuRecord item)
    {
        if (item == null) return;
        if (item.schemaVersion <= 0) item.schemaVersion = 1;
        if (item.boardSize <= 0) item.boardSize = 19;
        if (item.title == null) item.title = "";
        if (item.playedAt == null) item.playedAt = "";
        if (item.mode == null) item.mode = "";
    }

    public static void Add(KifuRecord rec)
    {
        TryAdd(rec, out _);
    }

    public static bool TryAdd(KifuRecord rec, out string error)
    {
        error = null;
        EnsureLoaded();
        if (rec != null) MigrateRecord(rec);
        if (!ValidateRecord(rec, out error)) return false;

        if (string.IsNullOrEmpty(rec.id)) rec.id = Guid.NewGuid().ToString("N");
        cache.items.Add(rec);
        return Save(out error);
    }

    public static bool Update(KifuRecord record)
    {
        EnsureLoaded();
        if (record == null || string.IsNullOrEmpty(record.id)) return false;
        MigrateRecord(record);
        if (!ValidateRecord(record, out _)) return false;

        for (int i = 0; i < cache.items.Count; i++)
        {
            if (cache.items[i] == null || cache.items[i].id != record.id) continue;
            KifuRecord previous = cache.items[i];
            cache.items[i] = record;
            if (Save(out _)) return true;
            cache.items[i] = previous;
            return false;
        }
        return false;
    }

    public static bool Delete(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < cache.items.Count; i++)
        {
            if (cache.items[i] == null || cache.items[i].id != id) continue;
            KifuRecord removed = cache.items[i];
            cache.items.RemoveAt(i);
            if (Save(out _)) return true;
            cache.items.Insert(i, removed);
            return false;
        }
        return false;
    }

    public static string BuildSgf(KifuRecord record)
    {
        record = record ?? new KifuRecord();
        int size = IsSupportedBoardSize(record.boardSize) ? record.boardSize : 19;
        var sb = new System.Text.StringBuilder(256);
        sb.Append("(;GM[1]FF[4]CA[UTF-8]")
            .Append("SZ[").Append(size.ToString(CultureInfo.InvariantCulture)).Append(']')
            .Append("KM[").Append(record.komi.ToString("0.0###", CultureInfo.InvariantCulture)).Append(']')
            .Append("PB[").Append(EscapeSgf(record.blackName)).Append(']')
            .Append("BR[").Append(EscapeSgf(record.blackRank)).Append(']')
            .Append("PW[").Append(EscapeSgf(record.whiteName)).Append(']')
            .Append("WR[").Append(EscapeSgf(record.whiteRank)).Append(']')
            .Append("RE[").Append(EscapeSgf(record.result)).Append(']')
            .Append("DT[").Append(EscapeSgf(record.playedAt)).Append(']')
            .Append("C[").Append(EscapeSgf(record.description)).Append(']');

        if (!string.IsNullOrEmpty(record.moveLogCsv))
        {
            foreach (string token in record.moveLogCsv.Split('|'))
            {
                if (string.IsNullOrEmpty(token)) continue;
                string[] parts = token.Split(',');
                if (parts.Length < 3
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int color))
                    continue;
                string side = color == 2 ? "W" : "B";
                sb.Append(';').Append(side).Append('[');
                if (x >= 0 && y >= 0 && x < size && y < size
                    && x < 26 && y < 26)
                    sb.Append((char)('a' + x)).Append((char)('a' + y));
                sb.Append(']');
            }
        }
        sb.Append(')');
        return sb.ToString();
    }

    static string EscapeSgf(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\").Replace("]", "\\]")
            .Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public static void UpdateAnalysis(string id, string analysisJson)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return;
        for (int i = 0; i < cache.items.Count; i++)
        {
            if (cache.items[i].id == id)
            {
                cache.items[i].analysisJson = analysisJson;
                Save(out _);
                break;
            }
        }
    }

    public static void UpdateCommentsAndBranches(string id, string commentsJson, string branchesJson)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return;
        for (int i = 0; i < cache.items.Count; i++)
        {
            if (cache.items[i].id == id)
            {
                cache.items[i].commentsJson = commentsJson;
                cache.items[i].branchesJson = branchesJson;
                Save(out _);
                break;
            }
        }
    }

    static bool Matches(KifuRecord r, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        return Contains(r.title, q)
            || Contains(r.blackName, q)
            || Contains(r.whiteName, q)
            || Contains(r.blackRank, q)
            || Contains(r.whiteRank, q)
            || Contains(r.description, q)
            || Contains(r.result, q)
            || Contains(r.playedAt, q)
            || Contains(r.mode, q);
    }

    static bool Contains(string s, string q)
    {
        return !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int CompareRecords(KifuRecord a, KifuRecord b, KifuSort sort)
    {
        int result;
        switch (sort)
        {
            case KifuSort.PlayedAtAscending:
                result = ComparePlayedAt(a.playedAt, b.playedAt);
                break;
            case KifuSort.MoveCountDescending:
                result = b.moveCount.CompareTo(a.moveCount);
                break;
            case KifuSort.Result:
                result = string.Compare(a.result ?? "", b.result ?? "", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                result = ComparePlayedAt(b.playedAt, a.playedAt);
                break;
        }
        if (result != 0) return result;
        result = string.Compare(a.playedAt ?? "", b.playedAt ?? "", StringComparison.OrdinalIgnoreCase);
        if (result != 0) return sort == KifuSort.PlayedAtDescending ? -result : result;
        return string.Compare(a.id ?? "", b.id ?? "", StringComparison.OrdinalIgnoreCase);
    }

    static int ComparePlayedAt(string left, string right)
    {
        bool leftOk = DateTime.TryParse(left, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime leftDate);
        bool rightOk = DateTime.TryParse(right, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime rightDate);
        if (leftOk && rightOk) return leftDate.CompareTo(rightDate);
        if (leftOk) return 1;
        if (rightOk) return -1;
        return string.Compare(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    }

    static void MigrateCatalog(KifuCatalog catalog)
    {
        if (catalog == null) return;
        if (catalog.schemaVersion <= 0) catalog.schemaVersion = 1;
        if (catalog.items == null) catalog.items = new List<KifuRecord>();

        foreach (var item in catalog.items)
        {
            if (item == null) continue;
            MigrateRecord(item);
        }

        // Legacy JSON can contain sizes that the fixed 19x19 rules/hash tables
        // cannot represent. Remove those records before history/replay callers
        // can construct GoRules with them.
        for (int i = catalog.items.Count - 1; i >= 0; i--)
        {
            KifuRecord item = catalog.items[i];
            if (item != null && !ValidateRecord(item, out string error))
            {
                Debug.LogWarning("[KifuDatabase] 忽略无效棋谱记录 " + (item.id ?? "<无 id>") + ": " + error);
                catalog.items.RemoveAt(i);
            }
        }
    }

    public static void EnsureLoaded()
    {
        if (cache != null) return;
        cache = new KifuCatalog();
        string path = FilePath;

        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrEmpty(json)) return;

            var loaded = JsonUtility.FromJson<KifuCatalog>(json);
            if (loaded != null && loaded.items != null)
            {
                MigrateCatalog(loaded);
                cache = loaded;
                return;
            }
            else
            {
                BackupCorruptedAndTryBak(path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[KifuDatabase] 读取棋谱库异常: " + ex);
            BackupCorruptedAndTryBak(path);
        }
    }

    static void BackupCorruptedAndTryBak(string path)
    {
        try
        {
            string corruptPath = path + ".corrupted." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".bak";
            if (File.Exists(path)) File.Copy(path, corruptPath, true);
            Debug.LogWarning("[KifuDatabase] 棋谱文件损坏，已备份至: " + corruptPath);

            string bakPath = path + ".bak";
            if (File.Exists(bakPath))
            {
                string bakJson = File.ReadAllText(bakPath);
                var loaded = JsonUtility.FromJson<KifuCatalog>(bakJson);
                if (loaded != null && loaded.items != null)
                {
                    MigrateCatalog(loaded);
                    cache = loaded;
                    Debug.Log("[KifuDatabase] 成功从备份文件恢复棋谱库");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[KifuDatabase] 恢复备份棋谱库失败: " + e);
        }
    }

    public static bool Save(out string error)
    {
        error = null;
        LastError = null;
        try
        {
            string path = FilePath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(cache, true);
            string tmpPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmpPath, json);

            string bakPath = path + ".bak";
            if (File.Exists(path))
            {
                try
                {
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Replace(tmpPath, path, bakPath);
                }
                catch
                {
                    File.Copy(path, bakPath, true);
                    File.Copy(tmpPath, path, true);
                    File.Delete(tmpPath);
                }
            }
            else
            {
                File.Move(tmpPath, path);
            }
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            LastError = error;
            Debug.LogError("[KifuDatabase] 保存棋谱库失败: " + e);
            return false;
        }
    }

    public static void ResetForTesting(KifuCatalog newCatalog = null)
    {
        cache = newCatalog ?? new KifuCatalog();
        MigrateCatalog(cache);
    }
}
