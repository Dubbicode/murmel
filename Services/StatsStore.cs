using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Murmel.Services;

public class StatsData
{
    /// <summary>Total number of words ever transcribed by Murmel for this user.
    /// Kept independent of HistoryStore (which caps at 200 entries and can be
    /// cleared by the user) so this number never goes down.</summary>
    public long TotalWordsSpoken { get; set; }

    /// <summary>Words spoken per day, keyed "yyyy-MM-dd" - drives the 7-day chart.
    /// Kept independent of HistoryStore for the same reason as TotalWordsSpoken: the
    /// 200-entry cap and "Verlauf leeren" must never erase a past day's count.</summary>
    public Dictionary<string, int> DailyWordCounts { get; set; } = new();
}

/// <summary>Persists lifetime usage stats to %AppData%\Typr\stats.json — separate
/// from the history file so "Verlauf leeren" never resets it and the old-entry
/// cap in HistoryStore never causes the total to shrink.</summary>
public class StatsStore
{
    private readonly string _filePath;

    public StatsData Data { get; private set; } = new();

    /// <summary>True when stats.json did not exist yet on load - i.e. this is the
    /// first time this counter has ever run. Used once, on app start, to backfill
    /// the total from whatever history entries already exist instead of starting
    /// existing users at 0.</summary>
    public bool WasCreatedFresh { get; private set; }

    public StatsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Typr");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "stats.json");
        Load();
    }

    public void AddWords(int count)
    {
        if (count <= 0) return;
        Data.TotalWordsSpoken += count;

        var key = DateKey(DateTime.Today);
        Data.DailyWordCounts.TryGetValue(key, out var existing);
        Data.DailyWordCounts[key] = existing + count;

        Save();
    }

    public int GetWordsForDay(DateTime day) =>
        Data.DailyWordCounts.TryGetValue(DateKey(day), out var count) ? count : 0;

    /// <summary>One-time backfill for existing users: called right after construction
    /// when WasCreatedFresh is true, so the lifetime total doesn't start at 0 just
    /// because this feature is new.</summary>
    public void SeedIfFresh(long words)
    {
        if (!WasCreatedFresh) return;
        Data.TotalWordsSpoken = words;
        Save();
    }

    /// <summary>Separate one-time backfill for the daily breakdown specifically (runs
    /// regardless of WasCreatedFresh, since this feature was added later than
    /// TotalWordsSpoken - existing users already have a stats.json, so WasCreatedFresh
    /// is false for them, but DailyWordCounts still starts empty). Populates from
    /// whatever history entries are currently on disk, grouped by day, so the 7-day
    /// chart doesn't show zero for days before today just because the feature is new.</summary>
    public void SeedDailyIfEmpty(IEnumerable<(DateTime Timestamp, int Words)> entries)
    {
        if (Data.DailyWordCounts.Count > 0) return;

        foreach (var (timestamp, words) in entries)
        {
            var key = DateKey(timestamp.Date);
            Data.DailyWordCounts.TryGetValue(key, out var existing);
            Data.DailyWordCounts[key] = existing + words;
        }
        Save();
    }

    private static string DateKey(DateTime day) => day.ToString("yyyy-MM-dd");

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Data = JsonSerializer.Deserialize<StatsData>(json) ?? new();
            }
            else
            {
                WasCreatedFresh = true;
            }
        }
        catch
        {
            Data = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // best-effort persistence; losing the lifetime counter is not fatal
        }
    }
}
