using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Murmel.Models;

namespace Murmel.Services;

public class NotesData
{
    public List<NoteEntry> Notes { get; set; } = new();

    // Column order on the board - user-managed, doesn't include the fixed
    // Inbox/Erledigt columns which always exist implicitly.
    public List<string> Projects { get; set; } = new();
}

/// <summary>Persists Kati's voice notes to notes.json - same load/save pattern as
/// DictionaryStore/HistoryStore/StatsStore, but the containing folder is configurable
/// (defaults to %AppData%\Typr like the others) so it can live in a Google Drive-synced
/// folder instead, for access from more than one PC.</summary>
public class NotesStore
{
    private string _filePath;

    public NotesData Data { get; private set; } = new();
    public string FolderPath { get; private set; }

    public NotesStore(string? customFolder = null)
    {
        FolderPath = string.IsNullOrWhiteSpace(customFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typr")
            : customFolder;
        Directory.CreateDirectory(FolderPath);
        _filePath = Path.Combine(FolderPath, "notes.json");
        Load();
    }

    /// <summary>Points the store at a different folder and writes the current in-memory
    /// data there right away, so switching to e.g. a Google Drive folder doesn't lose
    /// whatever was already saved locally.</summary>
    public void ChangeFolder(string newFolder)
    {
        Directory.CreateDirectory(newFolder);
        FolderPath = newFolder;
        _filePath = Path.Combine(newFolder, "notes.json");
        Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // best-effort; notes persistence failure should not crash the app
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Data = JsonSerializer.Deserialize<NotesData>(json) ?? new();
            }
        }
        catch
        {
            Data = new();
        }
    }
}
