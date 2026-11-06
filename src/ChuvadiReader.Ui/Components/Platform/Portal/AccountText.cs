namespace ChuvadiReader.Ui.Components.Platform.Portal;

/// <summary>How the portal shows a person's name: "AS" in the avatar, "Arun" on the chip (design 12b).</summary>
public static class AccountText
{
    /// <summary>First letters of the first and last words, upper-case: "Arun S." → "AS". "?" when empty.</summary>
    public static string Initials(string? displayName)
    {
        var words = Words(displayName);
        if (words.Length == 0) return "?";
        var first = char.ToUpperInvariant(words[0][0]);
        return words.Length == 1 ? first.ToString() : string.Concat(first, char.ToUpperInvariant(words[^1][0]));
    }

    /// <summary>The first word of the name: "Arun S." → "Arun". Empty when there is no name.</summary>
    public static string FirstName(string? displayName)
    {
        var words = Words(displayName);
        return words.Length == 0 ? string.Empty : words[0];
    }

    private static string[] Words(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? []
            : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Where(w => char.IsLetterOrDigit(w[0]))
               .ToArray();
}
