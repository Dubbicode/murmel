using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Murmel.Services;

/// <summary>
/// Applies Kati's personal dictionary to a freshly transcribed utterance. Design
/// mirrors how Wispr Flow and Superwhisper handle this (both apps were checked before
/// building this):
///   1. Corrections: whole-word, case-insensitive find/replace for terms the ASR
///      reliably mis-hears (e.g. "Claude"). Runs on every dictation, before anything
///      else - so downstream logic (self-correction detection, history) always sees
///      the fixed spelling. Whole-word matching only, so "Klod" doesn't also mangle a
///      word that merely contains it.
///   2. Snippets: voice-triggered text blocks. Two ways a trigger fires:
///      a) The ENTIRE utterance is just the trigger phrase, nothing else ("meine
///         E-Mail") - saying nothing else is a strong enough signal on its own, no
///         insert verb required.
///      b) Trigger + insert verb ("einfügen"/"hinzufügen"/"einsetzen", or "füge ...
///         ein/hinzu") appear ANYWHERE within a longer sentence - "Du kannst mich auch
///         unter meiner Handynummer hinzufügen erreichen" - only that span gets
///         replaced, the rest of the sentence is left exactly as dictated. The verb is
///         required here (unlike case a) because without it, the trigger words could
///         just be an ordinary part of a real dictation.
///      Both are matched letters-only (spaces/hyphens/punctuation stripped from BOTH
///      the utterance and the trigger before comparing) rather than word-for-word -
///      German compound words get transcribed inconsistently ("Handynummer" vs. "Handy
///      Nummer", "E-Mail-Adresse" vs. "E Mail Adresse") and the trigger was typed by
///      hand, so word-boundary differences alone shouldn't cause a miss.
/// </summary>
public static class DictionaryProcessor
{
    public static string ApplyCorrections(string text, DictionaryData dictionary)
    {
        foreach (var entry in dictionary.Corrections)
        {
            if (string.IsNullOrWhiteSpace(entry.Wrong) || string.IsNullOrWhiteSpace(entry.Right))
                continue;

            var pattern = @"\b" + Regex.Escape(entry.Wrong.Trim()) + @"\b";
            text = Regex.Replace(text, pattern, entry.Right.Trim(), RegexOptions.IgnoreCase);
        }
        return text;
    }

    // Natural synonyms people reach for when asking Murmel to insert something.
    private static readonly string[] InsertVerbs = { "einfügen", "hinzufügen", "einsetzen" };

    public static string ApplySnippets(string text, DictionaryData dictionary)
    {
        var whole = TryResolveWholeUtterance(text, dictionary);
        if (whole is not null) return whole;

        foreach (var entry in dictionary.Snippets)
        {
            if (string.IsNullOrWhiteSpace(entry.Trigger) || string.IsNullOrWhiteSpace(entry.Value))
                continue;

            // The Trigger field can hold several comma-separated synonyms for the same
            // value ("meine Handynummer, meine Telefonnummer, meine Nummer") - any one
            // of them fires the snippet.
            foreach (var synonym in entry.Trigger.Split(','))
            {
                var trigger = synonym.Trim();
                if (trigger.Length == 0) continue;

                var replaced = TryReplaceEmbeddedTrigger(text, trigger, entry.Value);
                if (replaced is not null)
                {
                    text = replaced;
                    break; // this entry fired - move on to the next dictionary entry
                }
            }
        }
        return text;
    }

    /// <summary>Case (a): the whole utterance (ignoring trailing punctuation) is just
    /// the trigger.</summary>
    private static string? TryResolveWholeUtterance(string text, DictionaryData dictionary)
    {
        var trimmed = CompactLetters(text.Trim().TrimEnd('.', '!', '?'));
        if (trimmed.Length == 0) return null;

        foreach (var entry in dictionary.Snippets)
        {
            if (string.IsNullOrWhiteSpace(entry.Trigger) || string.IsNullOrWhiteSpace(entry.Value))
                continue;

            foreach (var synonym in entry.Trigger.Split(','))
            {
                var trigger = CompactLetters(synonym.Trim());
                if (trigger.Length > 0 && trimmed == trigger)
                    return entry.Value;
            }
        }
        return null;
    }

    /// <summary>Case (b): finds "trigger+verb" (either order, or "füge trigger
    /// ein/hinzu") anywhere in the text by comparing letters-only, then maps the match
    /// back to the real span in the original text to replace just that part.</summary>
    private static string? TryReplaceEmbeddedTrigger(string text, string trigger, string value)
    {
        var (compact, map) = BuildLetterMap(text);
        var compactTrigger = CompactLetters(trigger);
        if (compactTrigger.Length == 0) return null;

        foreach (var verb in InsertVerbs)
        {
            int idx = compact.IndexOf(compactTrigger + verb, StringComparison.Ordinal);
            int len = compactTrigger.Length + verb.Length;
            if (idx < 0)
            {
                idx = compact.IndexOf(verb + compactTrigger, StringComparison.Ordinal);
            }
            if (idx >= 0)
                return ReplaceSpan(text, map, idx, len, value);
        }

        foreach (var tail in new[] { "ein", "hinzu" })
        {
            var needle = "füge" + compactTrigger + tail;
            int idx = compact.IndexOf(needle, StringComparison.Ordinal);
            if (idx >= 0)
                return ReplaceSpan(text, map, idx, needle.Length, value);
        }

        return null;
    }

    private static string ReplaceSpan(string text, int[] map, int compactStart, int compactLength, string value)
    {
        int origStart = map[compactStart];
        int origEnd = map[compactStart + compactLength - 1]; // inclusive index of the last matched original char
        return text[..origStart] + value + text[(origEnd + 1)..];
    }

    /// <summary>Lowercases and keeps only letters/digits, tracking - for each kept
    /// character - its index in the original string, so a match found in the compact
    /// form can be mapped back to a real span for replacement.</summary>
    private static (string Compact, int[] Map) BuildLetterMap(string s)
    {
        var sb = new StringBuilder(s.Length);
        var map = new int[s.Length];
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            var c = char.ToLowerInvariant(s[i]);
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                map[n++] = i;
            }
        }
        Array.Resize(ref map, n);
        return (sb.ToString(), map);
    }

    private static string CompactLetters(string s) => BuildLetterMap(s).Compact;
}
