using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Murmel.Services;

/// <summary>A parsed voice-correction command: "replace Find with Replace" in the
/// most recently dictated text.</summary>
public record CorrectionCommand(string Find, string Replace);

/// <summary>
/// Recognizes a small set of spoken correction phrases (WisprFlow-style "Command Mode",
/// but purely local pattern matching - no LLM involved, so it only handles a literal
/// word/phrase swap, not free-form rewriting).
///
/// Two distinct situations are handled:
///   1. SAME-utterance self-correction: the user corrects themselves mid-recording,
///      e.g. "Wir treffen uns am Montag. Nein, am Dienstag." - both the original
///      statement and the fix arrive as ONE transcribed blob. See
///      <see cref="TryParseEmbeddedCorrection"/>.
///   2. CROSS-utterance correction: the user notices a mistake only AFTER already
///      finishing (and possibly pasting) a dictation, and issues a standalone
///      correction command as its own separate recording, e.g. "korrigiere Montag
///      zu Dienstag". See <see cref="TryParse"/>.
/// </summary>
public static class CorrectionCommandParser
{
    // Spoken corrections are rarely the very first thing out of your mouth - "Nein,
    // korrigiere..." / "Moment, ändere..." is natural. Allow (and ignore) a run of common
    // filler/interjection words before the actual command verb, rather than requiring the
    // utterance to start with it.
    private const string FillerPrefix = @"^(?:\s*(?:nein|ne|äh|ähm|hm|moment|warte|ach|okay|ok)\b[,\.]?\s*)*";

    // Just the interjection part (no "nein"), for spots where "nein/ne/nee" itself is
    // handled separately right after - e.g. "Äh, nein, Dienstag" said before the actual
    // correction ("äh" is extremely common before someone catches their own mistake).
    private const string Interjection = @"(?:äh|ähm|hm|moment|warte|ach|okay|ok)\b[,\.]?\s*";

    // Extra filler words tolerated between the interjection and the actual replacement
    // value ("Nein, bitte Dienstag" / "Nein, eigentlich Dienstag").
    private const string ValueFiller = @"(?:bitte\s+|eigentlich\s+|doch\s+|vielmehr\s+|stattdessen\s+)?";

    // Full form: both what to find AND what to replace it with are spoken explicitly -
    // "korrigiere Montag zu Dienstag". Tolerant of a trailing period/exclamation mark,
    // since Parakeet sometimes adds terminal punctuation.
    private static readonly Regex[] FullPatterns =
    {
        new(FillerPrefix + @"korrigiere\s+(.+?)\s+(?:zu|in|auf)\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(FillerPrefix + @"ersetze\s+(.+?)\s+durch\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(FillerPrefix + @"ändere\s+(.+?)\s+(?:zu|in|auf)\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Short form: only the replacement is spoken, with a verb - "korrigiere zu
    // Dienstag" / "korrigiere das auf Dienstag" - a common, more natural shorthand when
    // it's obvious what's being corrected. The optional "das"/"es" placeholder covers
    // phrasing like "korrigiere das auf Dienstag". Resolved against the previous text
    // via GetImpliedFind.
    private static readonly Regex[] ShortPatterns =
    {
        new(FillerPrefix + @"korrigiere\s+(?:das\s+|es\s+)?(?:zu|in|auf)\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(FillerPrefix + @"ändere\s+(?:das\s+|es\s+)?(?:zu|in|auf)\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Bare form: no command verb at all, just an interjection and the replacement -
    // "Nein, am Dienstag." / "Nein, bitte Dienstag." This is by far the most natural
    // way people actually self-correct out loud, but also the riskiest to detect (a
    // real dictation can legitimately start with "Nein, ..."), so TryParse guards it
    // with a short-value-only check (see CountWords below).
    private static readonly Regex[] BarePatterns =
    {
        new(@"^(?:" + Interjection + @")*(?:nein|ne|nee)\b[,\.]?\s*" + ValueFiller + @"(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // A clause boundary that marks where an embedded self-correction starts within a
    // longer, single-recording utterance: the trigger word right after a sentence break
    // (or right at the very start of the text), optionally preceded by an interjection
    // like "Äh, nein, ..." - very natural when catching your own mistake mid-sentence.
    // NOTE: only a true sentence break (.!?) counts as a boundary - a comma does NOT,
    // specifically so that "Nein, korrigiere..." is found as ONE clause starting at
    // "Nein" rather than as two overlapping boundary matches ("Nein" and "korrigiere"
    // both preceded by a comma+space), which would otherwise chop the clause in half.
    private static readonly Regex ClauseBoundary = new(
        @"(?:^|(?<=[.!?]\s))(?:" + Interjection + @")*(?:nein|ne|nee|korrigiere|ändere|ersetze)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Weekdays =
    {
        "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag",
    };

    private static readonly Regex WeekdayPattern = new(
        @"\b(?:" + string.Join("|", Weekdays) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> LeadingPrepositions =
        new(StringComparer.OrdinalIgnoreCase) { "am", "im", "um", "zu", "in", "nach", "vor" };

    /// <summary>
    /// Looks for a self-correction embedded inside a SINGLE recorded utterance - e.g.
    /// "Wir treffen uns am Montag. Nein, am Dienstag." said in one continuous take.
    /// Returns the cleaned-up final text plus the command that was applied, or null if
    /// no correction clause is present. Unlike <see cref="TryParse"/>, this does not
    /// touch history - the caller treats the returned text as the (corrected) dictation.
    /// </summary>
    public static (string CleanedText, CorrectionCommand Command)? TryParseEmbeddedCorrection(string text)
    {
        var matches = ClauseBoundary.Matches(text);
        if (matches.Count == 0) return null;

        var last = matches[matches.Count - 1];
        // A trigger at index 0 means the WHOLE utterance is the command (nothing to
        // apply it to here) - that's the cross-utterance case, handled by TryParse
        // against the previous dictation instead.
        if (last.Index == 0) return null;

        var beforeText = text[..last.Index].TrimEnd();
        var clause = text[last.Index..].Trim();
        if (beforeText.Length == 0) return null;

        var command = TryParse(clause, beforeText);
        if (command is null) return null;

        var corrected = ApplyToText(beforeText, command);
        return corrected is null ? null : (corrected, command);
    }

    /// <param name="text">The freshly transcribed utterance to check.</param>
    /// <param name="previousText">The most recently dictated text (before this utterance),
    /// used to resolve the short/bare forms' implicit "find". Pass null if there is none.</param>
    public static CorrectionCommand? TryParse(string text, string? previousText)
    {
        var impliedFind = GetImpliedFind(previousText);

        // Short patterns are checked BEFORE full patterns: "korrigiere das auf Dienstag"
        // also technically matches a FullPattern (find="das", replace="Dienstag"), but
        // "das"/"es" there is a placeholder pronoun ("correct IT to Tuesday"), not a
        // literal word to search for - the short-form reading is the correct one. Full
        // patterns still win for anything ShortPatterns doesn't match, e.g. a real
        // multi-word find phrase like "korrigiere das Datum auf Dienstag".
        if (impliedFind is not null)
        {
            foreach (var pattern in ShortPatterns)
            {
                var match = pattern.Match(text);
                if (!match.Success) continue;

                var replace = StripQuotes(match.Groups[1].Value.Trim());
                if (replace.Length > 0)
                    return new CorrectionCommand(impliedFind, replace);
            }
        }

        foreach (var pattern in FullPatterns)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;

            var find = StripQuotes(match.Groups[1].Value.Trim());
            var replace = StripQuotes(match.Groups[2].Value.Trim());
            if (find.Length > 0 && replace.Length > 0)
                return new CorrectionCommand(find, replace);
        }

        if (impliedFind is not null)
        {
            foreach (var pattern in BarePatterns)
            {
                var match = pattern.Match(text);
                if (!match.Success) continue;

                var replace = StripLeadingPreposition(StripQuotes(match.Groups[1].Value.Trim()));
                // A real dictation can legitimately start with "Nein, ..." too - only
                // treat this as a correction when what follows is short (a name, a day,
                // a number), not a full sentence.
                if (replace.Length > 0 && CountWords(replace) <= 4)
                    return new CorrectionCommand(impliedFind, replace);
            }
        }

        return null;
    }

    private static string? ApplyToText(string original, CorrectionCommand command)
    {
        int idx = original.LastIndexOf(command.Find, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var result = (original[..idx] + command.Replace + original[(idx + command.Find.Length)..]).TrimEnd();
        if (result.Length > 0 && !".!?".Contains(result[^1]))
            result += ".";
        return result;
    }

    private static string StripQuotes(string s) => s.Trim('"', '„', '“', '\'', '‚', '‘');

    private static string StripLeadingPreposition(string value)
    {
        var spaceIdx = value.IndexOf(' ');
        if (spaceIdx < 0) return value;

        var firstWord = value[..spaceIdx];
        return LeadingPrepositions.Contains(firstWord) ? value[(spaceIdx + 1)..].TrimStart() : value;
    }

    private static int CountWords(string s) =>
        s.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Resolves what a short/bare correction implicitly refers to. Naive "last word of
    /// the sentence" is wrong more often than not for the most common real case - a
    /// weekday buried mid-sentence ("Wir treffen uns am Montag um 10" ends in "10", not
    /// "Montag") - so the last weekday mention is preferred when one is present;
    /// otherwise this falls back to the literal last word.
    /// </summary>
    private static string? GetImpliedFind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var weekdayMatches = WeekdayPattern.Matches(text);
        if (weekdayMatches.Count > 0)
            return weekdayMatches[weekdayMatches.Count - 1].Value;

        return GetLastWord(text);
    }

    private static string? GetLastWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim().TrimEnd('.', '!', '?', ',', ';', ':');
        int idx = trimmed.LastIndexOfAny(new[] { ' ', '\t', '\n' });
        var word = idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(word) ? null : word;
    }
}
