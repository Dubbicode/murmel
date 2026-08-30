namespace Murmel.Models;

/// <summary>A day-divider row interleaved into the history list between entries from
/// different days - e.g. "Heute", "Gestern", "28.08.2026".</summary>
public class HistoryDateHeader
{
    public string Label { get; set; } = string.Empty;
}
