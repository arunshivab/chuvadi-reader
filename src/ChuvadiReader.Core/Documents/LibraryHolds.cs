namespace ChuvadiReader.Core.Documents;

/// <summary>D-198: what the Reader holds back or changes until it is on the patched libraries (Pdf 3.35.1,
/// Sheets 2.2.1, Docs 2.4.1; library advisory D-405/D-406). Phase 2 takes those libraries in one move and turns
/// these off; each use names this class, so the list of what to undo is a search away.</summary>
public static class LibraryHolds
{
    /// <summary>Redaction is hidden: on Pdf 3.32.0 a redacted copy can still hold the text it should remove, and
    /// the library's own check reports it clean (RED-16/17/18). Protect's other tools stay.</summary>
    public static bool RedactionHeld => true;

    /// <summary>Said where redaction was.</summary>
    public const string RedactionBack = "Redaction comes back with the next update.";

    /// <summary>Why, in one line.</summary>
    public const string RedactionWhy =
        "Until then a redacted copy could still hold the text it should remove, so it is switched off. Stamp, watermark, flatten, password and signing are in Protect as before.";

    /// <summary>D-200: Excel's conditional formats are not evaluated, for every file, until the Sheets release that
    /// fixes SEC-08 and SEC-10 is taken: on Sheets 2.0.4 evaluating them can end the process (sec08) or run past a
    /// minute uncancellably (SEC-10). Cell values and their shown text are unchanged; only the conditional colours
    /// are not drawn, and a sheet that has them says so.</summary>
    public static bool ConditionalFormatsHeld => true;

    /// <summary>Said under a sheet that has conditional formats.</summary>
    public const string ConditionalColoursBack = "Conditional colours return with the next update";

    /// <summary>D-210 / D-211: text the saved file can't hold yet. Until Chuvadi.Pdf 3.36 routes marks, stamps,
    /// watermarks, headers, footers and page numbers through the embedded LiPi fonts, they are written with the
    /// standard PDF fonts, which hold plain Latin (ASCII and Latin-1: é, ñ, ß) and nothing else; dashes, curly
    /// quotes, €, Tamil and emoji came out as garbage or "?" (BUG-11, BUG-18). Such text is refused, naming the
    /// letters, before anything is written; the mark stays on screen.</summary>
    public static bool TextFontsHeld => true;

    /// <summary>The letters in <paramref name="text"/> the saved file can't hold yet, each once, in order.</summary>
    public static IReadOnlyList<string> LettersNotHeld(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text) || !TextFontsHeld) return found;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var el = (string)e.Current;
            bool ok = el.Length == 1 && el[0] is '\t' or '\n' or '\r' or (>= ' ' and <= '~') or (>= '\u00A0' and <= '\u00FF');
            if (!ok && !found.Contains(el)) found.Add(el);
        }

        return found;
    }

    /// <summary>The sentence refusing <paramref name="text"/>, or null when the file can hold all of it.</summary>
    public static string? TextNotHeld(string? text)
    {
        var letters = LettersNotHeld(text);
        if (letters.Count == 0) return null;
        var shown = string.Join(" ", letters.Take(10)) + (letters.Count > 10 ? $" and {letters.Count - 10} more" : "");
        return $"These letters can't be saved into the file yet: {shown}. They arrive with the next update. Nothing was saved; remove them to save now.";
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> with <see cref="TextNotHeld"/> when any of
    /// <paramref name="texts"/> can't be held.</summary>
    public static void CheckTextHeld(IEnumerable<string?> texts)
    {
        var all = string.Concat(texts.Where(t => !string.IsNullOrEmpty(t)));
        if (TextNotHeld(all) is { } refusal) throw new InvalidOperationException(refusal);
    }
}
