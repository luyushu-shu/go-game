using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class KifuRecord
{
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
}

[Serializable]
class KifuCatalog
{
    public List<KifuRecord> items = new List<KifuRecord>();
}

// 本地 JSON 棋谱库：写入 persistentDataPath，支持按关键字查询。
public static class KifuDatabase
{
    static string FilePath => Path.Combine(Application.persistentDataPath, "kifu_db.json");

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

    public static void Add(KifuRecord rec)
    {
        EnsureLoaded();
        if (rec == null) return;
        if (string.IsNullOrEmpty(rec.id)) rec.id = Guid.NewGuid().ToString("N");
        cache.items.Add(rec);
        Save();
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

    static void EnsureLoaded()
    {
        if (cache != null) return;
        cache = new KifuCatalog();
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrEmpty(json)) return;
            var loaded = JsonUtility.FromJson<KifuCatalog>(json);
            if (loaded != null && loaded.items != null) cache = loaded;
        }
        catch { }
    }

    static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(cache, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("保存棋谱库失败: " + e.Message);
        }
    }
}
