using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The exhibition's ranking: one record per team (Haru's player and Dudu's player), fastest
// clear first. Kept as JSON on the PC that hosts the games, so it survives going back to the
// title, new matches and restarting the program. The other PC never writes it.
public static class ExhibitionRanking
{
    public const int MaxStored = 100;
    public const int Shown = 10;
    public const int MaxNameLength = 8;
    private const string FileName = "exhibition_ranking.json";

    [Serializable]
    public sealed class Record
    {
        public string id;
        public string haruName;
        public string duduName;
        // Seconds, as measured by StageFlow; sorted on this, shown rounded.
        public double clearTime;
        public string recordedAt;
    }

    [Serializable]
    private sealed class Store
    {
        public List<Record> records = new List<Record>();
    }

    private static Store store;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static IReadOnlyList<Record> All
    {
        get
        {
            Load();
            return store.records;
        }
    }

    private static void Load()
    {
        if (store != null) return;
        store = new Store();
        try
        {
            if (File.Exists(FilePath))
            {
                Store read = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                if (read != null && read.records != null) store = read;
            }
        }
        catch (Exception e)
        {
            // A damaged file must not stop the exhibition; keep it aside and start fresh.
            Debug.LogException(e);
            try { File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch (Exception copyError) { Debug.LogException(copyError); }
        }
        store.records.RemoveAll(record => record == null);
        Sort();
    }

    private static void Sort()
    {
        // Equal times: the earlier record stays above. List.Sort is not stable, so compare order too.
        List<Record> list = store.records;
        Dictionary<Record, int> order = new Dictionary<Record, int>();
        for (int i = 0; i < list.Count; i++) order[list[i]] = i;
        list.Sort((a, b) =>
        {
            int byTime = a.clearTime.CompareTo(b.clearTime);
            return byTime != 0 ? byTime : order[a].CompareTo(order[b]);
        });
    }

    private static void Save()
    {
        try
        {
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(store, true));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporary, FilePath);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // Adds a team's record (once per id) and returns its rank, counting from 1.
    public static int Add(Record record)
    {
        Load();
        int existing = store.records.FindIndex(other => other.id == record.id);
        if (existing >= 0) return existing + 1;
        store.records.Add(record);
        Sort();
        int rank = store.records.IndexOf(record) + 1;
        // Keep the best; the team just added is still told its rank even if it falls off the end.
        if (store.records.Count > MaxStored) store.records.RemoveRange(MaxStored, store.records.Count - MaxStored);
        Save();
        return rank;
    }

    public static List<Record> Top(int count = Shown)
    {
        Load();
        return store.records.GetRange(0, Mathf.Min(count, store.records.Count));
    }

    // For operators only: not reachable from anything a visitor can click.
    public static void ResetAll()
    {
        Load();
        if (store.records.Count > 0)
        {
            try { File.Copy(FilePath, FilePath + ".reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch (Exception e) { Debug.LogException(e); }
        }
        store.records.Clear();
        Save();
        Debug.Log("Exhibition ranking reset (the old file was kept next to it).");
    }

    // Letters (Korean, Latin) and digits only, no spaces at either end, at most MaxNameLength.
    public static string CleanName(string name)
    {
        if (name == null) return string.Empty;
        System.Text.StringBuilder kept = new System.Text.StringBuilder();
        foreach (char c in name.Trim())
        {
            if (!IsAllowed(c)) continue;
            if (kept.Length >= MaxNameLength) break;
            kept.Append(c);
        }
        return kept.ToString().Trim();
    }

    public static bool IsAllowed(char c) =>
        char.IsLetterOrDigit(c) || c == ' ' || (c >= 0x3131 && c <= 0x318E) || (c >= 0xAC00 && c <= 0xD7A3);

    public static string FormatTime(double seconds, bool hundredths = true)
    {
        if (seconds < 0) seconds = 0;
        int whole = (int)Math.Floor(seconds);
        string text = $"{whole / 60:00}:{whole % 60:00}";
        if (hundredths) text += $".{(int)Math.Floor((seconds - whole) * 100):00}";
        return text;
    }
}
