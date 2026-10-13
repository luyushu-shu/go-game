using System;
using System.IO;
using UnityEngine;

// 负责打谱进度持久化、容灾读取与棋谱数据管理
public static class GoSaveManager
{
    public const string StudySaveKey = "GoStudySave";
    public static string StudySavePath => Path.Combine(Application.persistentDataPath, "study_save.json");

    public static bool SafeWriteText(string path, string content, out string error)
    {
        error = null;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmpPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmpPath, content);

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
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.LogError($"[GoSaveManager] 写入文件失败 {path}: {ex}");
            return false;
        }
    }

    public static bool SaveStudyProgress(GoRules rules, out string error)
    {
        error = null;
        if (rules == null)
        {
            error = "对局规则为空，无法保存";
            return false;
        }

        try
        {
            var data = rules.ExportState();
            data.phase = (int)GoPhase.Play;
            data.passes = 0;
            data.result = null;
            data.dead = new int[0];
            string json = JsonUtility.ToJson(data);

            // 双重持久化：PlayerPrefs + 磁盘文件
            PlayerPrefs.SetString(StudySaveKey, json);
            PlayerPrefs.Save();

            return SafeWriteText(StudySavePath, json, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.LogError("[GoSaveManager] 保存打谱进度异常: " + ex);
            return false;
        }
    }

    public static bool TryLoadStudySave(out GoStateData data, out string message)
    {
        data = null;
        message = null;

        GoStateData fileSave = null;
        if (File.Exists(StudySavePath))
        {
            try
            {
                string json = File.ReadAllText(StudySavePath);
                fileSave = ParseStudyJson(json);
            }
            catch (Exception ex)
            {
                message = "读取打谱磁盘存档失败: " + ex.Message;
                Debug.LogWarning("[GoSaveManager] " + message);
                BackupCorruptedFile(StudySavePath);
            }
        }

        GoStateData prefsSave = null;
        if (PlayerPrefs.HasKey(StudySaveKey))
        {
            try
            {
                string pJson = PlayerPrefs.GetString(StudySaveKey);
                prefsSave = ParseStudyJson(pJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GoSaveManager] 读取 PlayerPrefs 存档失败: " + ex.Message);
            }
        }

        data = PickRicherSave(fileSave, prefsSave);
        string valErr = null;
        if (data != null && ValidateStateData(data, out valErr))
        {
            MigrateStateData(data);
            return true;
        }

        if (data != null && !string.IsNullOrEmpty(valErr))
        {
            message = "存档数据校验未通过: " + valErr;
        }
        data = null;
        return false;
    }

    public static void ClearStudySave()
    {
        try
        {
            PlayerPrefs.DeleteKey(StudySaveKey);
            PlayerPrefs.Save();
            if (File.Exists(StudySavePath)) File.Delete(StudySavePath);
            string bak = StudySavePath + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GoSaveManager] 清理打谱存档异常: " + ex);
        }
    }

    public static bool ValidateStateData(GoStateData d, out string error)
    {
        error = null;
        if (d == null) { error = "存档为空"; return false; }
        if (d.size != 9 && d.size != 13 && d.size != 19)
        {
            error = $"棋盘尺寸非法: {d.size}";
            return false;
        }
        int expected = d.size * d.size;
        if (d.board != null && d.board.Length != expected)
        {
            error = $"棋盘数组长度不匹配: {d.board.Length} vs {expected}";
            return false;
        }
        return true;
    }

    static void MigrateStateData(GoStateData d)
    {
        if (d == null) return;
        if (d.schemaVersion <= 0) d.schemaVersion = 1;
        if (d.board == null && !string.IsNullOrEmpty(d.boardCsv))
        {
            // boardCsv will be converted in GoRules.ImportState
        }
    }

    static GoStateData ParseStudyJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var d = JsonUtility.FromJson<GoStateData>(json);
        return d != null && d.size > 0 ? d : null;
    }

    static GoStateData PickRicherSave(GoStateData a, GoStateData b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return CountMoveNums(b) > CountMoveNums(a) ? b : a;
    }

    static int CountMoveNums(GoStateData d)
    {
        if (d == null) return 0;
        int n = 0;
        if (!string.IsNullOrEmpty(d.moveAtCsv))
            foreach (var p in d.moveAtCsv.Split(','))
                if (int.TryParse(p, out int v) && v > 0) n++;
        if (!string.IsNullOrEmpty(d.moveLogCsv))
            n = Math.Max(n, d.moveLogCsv.Split('|').Length);
        if (d.moveCount > n) n = d.moveCount;
        return n;
    }

    static void BackupCorruptedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string corruptPath = path + ".corrupted." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".bak";
                File.Copy(path, corruptPath, true);
            }
        }
        catch { }
    }
}
