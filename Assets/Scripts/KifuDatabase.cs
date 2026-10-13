using System;
using System.Collections.Generic;
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

// 本地 JSON 棋谱库：写入 persistentDataPath，支持按关键字查询与原子安全写入。
public static class KifuDatabase
{
    public static string FilePath => Path.Combine(Application.persistentDataPath, "kifu_db.json");
    public static string LastError { get; private set; }

    static KifuCatalog cache;

    public static IReadOnlyList<KifuRecord> All()
    {
        EnsureLoaded();
        return cache.items;
    }

    public static List<KifuRecord> Query(string keyword)
    {
        EnsureLoaded();
        var list = new List<KifuRecord>();
        string q = (keyword ?? "").Trim();
        for (int i = cache.items.Count - 1; i >= 0; i--)
        {
            var r = cache.items[i];
            if (Matches(r, q)) list.Add(r);
        }
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
        if (rec.boardSize != 9 && rec.boardSize != 13 && rec.boardSize != 19)
        {
            if (rec.boardSize <= 0 || rec.boardSize > 25)
            {
                error = $"不支持的棋盘路数: {rec.boardSize}";
                return false;
            }
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

    static void MigrateCatalog(KifuCatalog catalog)
    {
        if (catalog == null) return;
        if (catalog.schemaVersion <= 0) catalog.schemaVersion = 1;
        if (catalog.items == null) catalog.items = new List<KifuRecord>();

        foreach (var item in catalog.items)
        {
            if (item == null) continue;
            if (item.schemaVersion <= 0) item.schemaVersion = 1;
            if (item.boardSize <= 0) item.boardSize = 19;
            if (item.title == null) item.title = "";
            if (item.playedAt == null) item.playedAt = "";
            if (item.mode == null) item.mode = "";
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
    }
}
