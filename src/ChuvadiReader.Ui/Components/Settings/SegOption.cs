namespace ChuvadiReader.Ui.Components.Settings;

/// <summary>One option of a <see cref="Segmented"/> control.</summary>
public sealed record SegOption(string Value, string Label)
{
    /// <summary>Options whose value and label are the same text.</summary>
    public static IReadOnlyList<SegOption> Of(params string[] labels)
        => labels.Select(l => new SegOption(l, l)).ToArray();
}
