using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Murmel.Models;

namespace Murmel.Services;

/// <summary>
/// Persists transcription history to a small local JSON file under
/// %AppData%\Typr\history.json — nothing leaves the machine, matching the
/// "no cloud" requirement.
/// </summary>
public class HistoryStore
{
    private readonly string _filePath;
    private const int MaxEntries = 200;

    public List<HistoryEntry> Entries { get; private set; } = new();

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Typr");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Entries.Insert(0, new HistoryEntry { Timestamp = DateTime.Now, Text = text });
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);

        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new();
            }
        }
        catch
        {
            // corrupted/unreadable history file should never crash the app - just start fresh
            Entries = new();
        }
    }

    /// <summary>Persists the current Entries to disk. Public so a caller that mutates an
    /// existing entry in place (e.g. a voice-correction command fixing the last dictation)
    /// can save that change without needing an Add/Clear-shaped API.</summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // best-effort persistence; losing history is not fatal
        }
    }
}
