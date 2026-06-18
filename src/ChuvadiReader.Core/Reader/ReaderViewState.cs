namespace ChuvadiReader.Core.Reader;

/// <summary>How the page canvas is auto-sized. Free = manual zoom; Width/Page recompute on resize.</summary>
public enum ReaderFitMode
{
    Free,
    Width,
    Page,
}

/// <summary>Page arrangement in the reader desk.</summary>
public enum ReaderLayout
{
    Single,
    Continuous,
    TwoPage,
    TwoPageContinuous,
}

/// <summary>Active pointer tool in the reader.</summary>
public enum ReaderTool
{
    None,
    Hand,
    Marquee,
    Loupe,
}
