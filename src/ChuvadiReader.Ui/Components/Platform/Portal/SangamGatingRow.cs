namespace ChuvadiReader.Ui.Components.Platform.Portal;

/// <summary>One row of the "What needs Sangam" list (design 12c).</summary>
/// <param name="Icon">Icon name.</param>
/// <param name="Text">What the row is about.</param>
/// <param name="NeedsSangam">True: needs a Sangam identity. False: free and local.</param>
public sealed record SangamGatingRow(string Icon, string Text, bool NeedsSangam)
{
    /// <summary>
    /// The handout's list (12c) for this release. "Share a link" is left out: sharing is not in this
    /// release (Arun, 6 Nov).
    /// </summary>
    public static IReadOnlyList<SangamGatingRow> Release1 { get; } =
    [
        new("reading-mode", "Read, find, print", false),
        new("edit", "Basic markup: text, shapes, highlight", false),
        new("grid-view", "Arrange, bind, split, compress", false),
        new("redact", "Redact, stamp, flatten, password", false),
        new("certification", "Sign with a certificate", true),
        new("refresh", "Sync across devices", true),
        new("library", "Anything on the web portal", true),
    ];
}
