namespace ChuvadiReader.Core.Documents;

/// <summary>The Home greeting line ("Good morning, Arun.").</summary>
public static class GreetingText
{
    /// <summary>
    /// "Good {part}, {name}." — the name is shown exactly as the person typed it. When it already
    /// ends with a full stop ("Arun S."), the greeting doesn't add a second one.
    /// </summary>
    public static string For(int hour, string? name)
    {
        var part = hour < 12 ? "morning" : hour < 17 ? "afternoon" : "evening";
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"Good {part}.";
        }

        return name.TrimEnd().EndsWith('.') ? $"Good {part}, {name}" : $"Good {part}, {name}.";
    }
}
