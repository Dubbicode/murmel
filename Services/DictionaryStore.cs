using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Murmel.Services;

/// <summary>A word/phrase the ASR reliably mis-hears, and what to replace it with -
/// e.g. Wrong="Klod", Right="Claude". Applied as a whole-word, case-insensitive
/// find/replace to every fresh transcription.</summary>
public class CorrectionEntry
{
    public string Wrong { get; set; } = "";
    public string Right { get; set; } = "";
}

/// <summary>A voice-triggered text snippet - saying the Trigger phrase (optionally
/// with "einfügen") inserts Value instead, e.g. Trigger="meine E-Mail",
/// Value="kati@example.com".</summary>
public class SnippetEntry
{
    public string Trigger { get; set; } = "";
    public string Value { get; set; } = "";
}

public class DictionaryData
{
    public List<CorrectionEntry> Corrections { get; set; } = new();
    public List<SnippetEntry> Snippets { get; set; } = new();
}

/// <summary>Persists Kati's personal correction/snippet dictionary to
/// %AppData%\Typr\dictionary.json - purely local, same as history and settings.</summary>
public class DictionaryStore
{
    private readonly string _filePath;

    public DictionaryData Data { get; private set; } = new();

    public DictionaryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Typr");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "dictionary.json");
        Load();
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
            // best-effort; dictionary persistence failure should not crash the app
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Data = JsonSerializer.Deserialize<DictionaryData>(json) ?? new();
            }
        }
        catch
        {
            Data = new();
        }
    }
}
