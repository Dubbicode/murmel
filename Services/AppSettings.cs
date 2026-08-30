using System;
using System.IO;
using System.Text.Json;

namespace Murmel.Services;

/// <summary>How the hotkey triggers a recording.</summary>
public enum RecordingMode
{
    /// <summary>Hold the hotkey down to record, release to stop (the original behavior).</summary>
    PushToTalk,

    /// <summary>Press the hotkey once to start recording, press it again to stop -
    /// no need to hold it down.</summary>
    Toggle
}

public class AppSettingsData
{
    public bool AutoPasteIntoActiveWindow { get; set; } = true;
    public HotkeyPreset Hotkey { get; set; } = HotkeyPreset.CtrlShift;
    public RecordingMode RecordingMode { get; set; } = RecordingMode.PushToTalk;
    public bool StartMinimizedToTray { get; set; } = false;

    // Null = use the default bottom-center placement. Set once the user drags the
    // small background indicator to a spot they prefer.
    public double? IndicatorPositionX { get; set; }
    public double? IndicatorPositionY { get; set; }
}

/// <summary>Loads/saves app settings to %AppData%\Typr\settings.json.</summary>
public class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsData Data { get; private set; } = new();

    public AppSettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Typr");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
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
            // best-effort; settings persistence failure should not crash the app
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                Data = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new();
            }
        }
        catch
        {
            Data = new();
        }
    }
}
