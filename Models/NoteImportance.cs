namespace Murmel.Models;

/// <summary>How urgently a note needs attention - shown as a fixed lane within its
/// project column on the Notizen board. Set purely by dragging (or the equivalent
/// click controls), never by voice.</summary>
public enum NoteImportance
{
    Wichtig,
    Normal,
    Unwichtig
}
