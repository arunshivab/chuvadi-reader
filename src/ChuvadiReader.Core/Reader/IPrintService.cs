using System.Globalization;

namespace ChuvadiReader.Core.Reader;

/// <summary>What to print.</summary>
public sealed record PrintRequest
{
    /// <summary>Pages as typed: "" or "all" = every page, "current" = <see cref="CurrentPage"/>,
    /// otherwise a list such as "1-3, 5" (1-based, inclusive).</summary>
    public string PageRange { get; init; } = string.Empty;

    /// <summary>Zero-based page the reader shows (used for "current").</summary>
    public int CurrentPage { get; init; }

    /// <summary>Copies to preselect in the dialog (the user can still change it). Null = printer default.</summary>
    public int? Copies { get; init; }

    /// <summary>Scale each page to the printable area (true) or print at actual size, centred (false).</summary>
    public bool FitToPage { get; init; } = true;

    /// <summary>Password for a protected file.</summary>
    public string? Password { get; init; }

    /// <summary>Name shown in the print queue; default the file name.</summary>
    public string? JobName { get; init; }
}

public enum PrintOutcome
{
    Printed,
    Cancelled,
    Unavailable,
    Failed,
}

/// <summary>Result of <see cref="IPrintService.PrintAsync"/>. <see cref="Message"/> is user-facing.</summary>
public sealed record PrintResult(PrintOutcome Outcome, int PagesSent, string? Message = null)
{
    public bool Succeeded => Outcome == PrintOutcome.Printed;
}

/// <summary>Native printing, implemented per host. Printing always uses the layers as the
/// file sets them (view-only layer toggles don't reach the printer).</summary>
public interface IPrintService
{
    /// <summary>False when the platform has no print path (the Print card is greyed out).</summary>
    bool IsAvailable { get; }

    /// <summary>Shows the native print dialog, then renders and sends the requested pages.
    /// Cancelling the token stops the job between pages.</summary>
    Task<PrintResult> PrintAsync(string pdfPath, PrintRequest request, CancellationToken ct = default);
}

/// <summary>Used where printing isn't available (and as the default before a host registers one).</summary>
public sealed class NoopPrintService : IPrintService
{
    public bool IsAvailable => false;

    public Task<PrintResult> PrintAsync(string pdfPath, PrintRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PrintResult(PrintOutcome.Unavailable, 0, "Printing isn't available on this platform"));
}

/// <summary>Parses page-range text ("1-3, 5", "all", "current") into zero-based page indices.</summary>
public static class PageRangeParser
{
    /// <summary>Parses <paramref name="text"/> against a document of <paramref name="pageCount"/> pages.
    /// Order is kept as typed; repeated pages are printed once. Returns false with a short
    /// user-facing <paramref name="error"/> when the text can't be used.</summary>
    public static bool TryParse(string? text, int pageCount, int currentPage, out IReadOnlyList<int> pages, out string? error)
    {
        pages = Array.Empty<int>();
        error = null;
        if (pageCount <= 0)
        {
            error = "The document has no pages.";
            return false;
        }

        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0 || t.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            pages = Enumerable.Range(0, pageCount).ToList();
            return true;
        }

        if (t.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            pages = new[] { Math.Clamp(currentPage, 0, pageCount - 1) };
            return true;
        }

        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var raw in t.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.Replace('–', '-').Replace('—', '-');
            int dash = part.IndexOf('-');
            int from, to;
            if (dash < 0)
            {
                if (!TryPage(part, out from))
                {
                    error = $"\"{raw}\" isn't a page number.";
                    return false;
                }

                to = from;
            }
            else
            {
                var a = part[..dash].Trim();
                var b = part[(dash + 1)..].Trim();
                if (a.Length == 0) a = "1";
                if (b.Length == 0) b = pageCount.ToString(CultureInfo.InvariantCulture);
                if (!TryPage(a, out from) || !TryPage(b, out to))
                {
                    error = $"\"{raw}\" isn't a page range.";
                    return false;
                }

                if (from > to)
                {
                    error = $"\"{raw}\" runs backwards.";
                    return false;
                }
            }

            if (from < 1 || to > pageCount)
            {
                error = pageCount == 1 ? "This document has 1 page." : $"Pages run from 1 to {pageCount}.";
                return false;
            }

            for (int p = from; p <= to; p++)
            {
                if (seen.Add(p - 1)) result.Add(p - 1);
            }
        }

        if (result.Count == 0)
        {
            error = "No pages selected.";
            return false;
        }

        pages = result;
        return true;
    }

    private static bool TryPage(string s, out int page) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out page);
}
