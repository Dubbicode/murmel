using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Murmel.Services;

/// <summary>Recognizes a spoken project-assignment prefix at the start of a note
/// recording ("Projekt X: ...", "Projektnummer X: ...", "neue Notiz für X, ...") and
/// matches X against Kati's own project list - purely local regex + string comparison
/// against a known list, no LLM/fuzzy guessing (same principle as
/// CorrectionCommandParser, which also tries a small family of phrasings in order). A
/// note with no recognized project stays untouched and lands in the Inbox - in
/// particular X has to already be a project Kati created; nothing gets auto-created from
/// voice alone.</summary>
public static class NoteProjectMatcher
{
    // The ASR renders the same spoken pause after the project name as a comma one time
    // and a period (or "!"/"?") the next - all of them count as the separator here,
    // not just ",".
    private static readonly Regex[] TriggerPatterns =
    {
        new(@"^\s*projektnummer\s+(?<name>.+?)\s*[:,.!?]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*projekt\s+(?<name>.+?)\s*[:,.!?]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(neue\s+)?notiz\s+für\s+(?<name>.+?)\s*[:,.!?]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    /// <summary>Returns the matched project name (in the exact casing from Kati's list)
    /// and the note text with the trigger phrase stripped off, or (null, the original
    /// text unchanged) if no known project was recognized at the start.</summary>
    public static (string? Project, string Text) Extract(string rawText, IReadOnlyList<string> knownProjects)
    {
        foreach (var pattern in TriggerPatterns)
        {
            var match = pattern.Match(rawText);
            if (!match.Success) continue;

            var spoken = match.Groups["name"].Value.Trim();
            var matched = FindProject(spoken, knownProjects);
            if (matched is null) continue;

            var remainder = rawText[match.Length..].Trim();
            return (matched, remainder.Length > 0 ? remainder : rawText);
        }
        return (null, rawText);
    }

    /// <summary>Project names can be long and specific ("AB25-005 Douglas Sells
    /// Convention"), so saying the whole thing every time isn't realistic - a shorter
    /// piece of it (the code "AB25-005", or a distinctive word like "Douglas") should
    /// still resolve to that project. Tries an exact match first; failing that, treats
    /// the spoken text as identifying the project if it occurs as a whole word/token
    /// somewhere in exactly one project's name. If it's a whole word in more than one
    /// project, that's ambiguous - not a match, rather than guessing which one.</summary>
    private static string? FindProject(string spoken, IReadOnlyList<string> knownProjects)
    {
        if (spoken.Length == 0) return null;

        var exact = knownProjects.FirstOrDefault(p => string.Equals(p, spoken, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var pattern = @"\b" + Regex.Escape(spoken) + @"\b";
        var candidates = knownProjects
            .Where(p => Regex.IsMatch(p, pattern, RegexOptions.IgnoreCase))
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
