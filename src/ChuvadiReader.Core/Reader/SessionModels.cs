namespace ChuvadiReader.Core.Reader;

/// <summary>The serialized form of a whole bench: the shelf of sources and every desk with its
/// pages and settings. Sources are stored by <em>path</em> (not embedded), so a session file is
/// tiny but depends on the source files still being where they were — a missing source is skipped
/// on restore (see <see cref="SessionImportResult"/>). The per-desk setting objects
/// (<see cref="DeskWatermark"/> etc.) are reused verbatim, since they are plain serializable POCOs.
/// </summary>
public sealed class BenchSessionDto
{
    /// <summary>Schema version, so future readers can migrate older files.</summary>
    public int Version { get; set; } = 1;

    public string? SavedAtUtc { get; set; }

    public List<SessionSourceDto> Sources { get; set; } = new();

    public List<SessionDeskDto> Desks { get; set; } = new();
}

/// <summary>A shelf source: the file it points at plus its non-destructive shelf state.</summary>
public sealed class SessionSourceDto
{
    public int Index { get; set; }

    public string Path { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public List<int>? PageFilter { get; set; }

    public bool Collapsed { get; set; }
}

/// <summary>A desk and its settings (no live state, just the persistent arrangement).</summary>
public sealed class SessionDeskDto
{
    public string Name { get; set; } = string.Empty;

    public bool NameLocked { get; set; }

    public string? ColorHex { get; set; }

    public string? NormalizeSize { get; set; }

    public bool AddBookmarks { get; set; } = true;

    public DeskWatermark? Watermark { get; set; }

    public DeskHeaderFooter? HeaderFooter { get; set; }

    public DeskNumbering? Numbering { get; set; }

    public List<SessionPageDto> Pages { get; set; } = new();
}

/// <summary>One page within a desk. References its source by path (matched on restore).</summary>
public sealed class SessionPageDto
{
    public int SourceIndex { get; set; }

    public int OriginalIndex { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public int Rotation { get; set; }

    public bool IsBlank { get; set; }

    public string? BackgroundHex { get; set; }

    public bool IsImage { get; set; }

    public string? ImagePath { get; set; }

    public CropRect? Crop { get; set; }

    public CropFit CropMode { get; set; } = CropFit.ToSize;

    public MarginSet? Margins { get; set; }
}

/// <summary>A reusable desk preset: the per-desk settings only, no pages. Stored in app-data so it
/// survives restarts and can be applied to any desk.</summary>
public sealed class DeskTemplateDto
{
    public string Name { get; set; } = string.Empty;

    public string? ColorHex { get; set; }

    public string? NormalizeSize { get; set; }

    public bool AddBookmarks { get; set; } = true;

    public DeskWatermark? Watermark { get; set; }

    public DeskHeaderFooter? HeaderFooter { get; set; }

    public DeskNumbering? Numbering { get; set; }
}

/// <summary>The outcome of restoring a session: what loaded, and what was skipped (with reasons).</summary>
public sealed class SessionImportResult
{
    public int SourcesLoaded { get; set; }

    public int SourcesMissing { get; set; }

    public int PagesSkipped { get; set; }

    public List<string> Warnings { get; } = new();

    public bool HasWarnings => Warnings.Count > 0;
}
