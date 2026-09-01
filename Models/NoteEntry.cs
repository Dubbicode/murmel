using System;

namespace Murmel.Models;

/// <summary>A single voice note captured via the dedicated notes hotkey.
/// Project == null means the note sits in the Inbox (no recognized "Projekt X: ..."
/// trigger, or not yet sorted by hand).</summary>
public class NoteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string? Project { get; set; }
    public NoteImportance Importance { get; set; } = NoteImportance.Normal;
    public bool IsDone { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
