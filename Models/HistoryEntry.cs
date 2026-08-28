using System;

namespace Murmel.Models;

public class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;

    public string DisplayTime => Timestamp.ToString("dd.MM. HH:mm");

    /// <summary>Short preview shown in the sidebar list.</summary>
    public string Preview => Text.Length > 60 ? Text[..60] + "…" : Text;
}
