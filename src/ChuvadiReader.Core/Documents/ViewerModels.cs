namespace ChuvadiReader.Core.Documents;

/// <summary>Position reported by a view-only viewer (D-09): it drives the top-bar counter
/// (‹ 2 / 13 ›). <see cref="Unit"/> is one of <see cref="Page"/>, <see cref="Section"/>,
/// <see cref="Line"/> or <see cref="Sheet"/>. <see cref="Current"/> and <see cref="Total"/> are 1-based.</summary>
public sealed record ViewerCounter(int Current, int Total, string Unit)
{
    public const string Page = "page";
    public const string Section = "section";
    public const string Line = "line";
    public const string Sheet = "sheet";
}

/// <summary>One entry of a viewer's outline (the Pages &amp; outline drawer). <see cref="Level"/>
/// starts at 1; <see cref="Anchor"/> is passed back to the viewer's <c>ScrollToAnchorAsync</c>.</summary>
public sealed record ViewerOutlineEntry(string Title, int Level, string Anchor);
