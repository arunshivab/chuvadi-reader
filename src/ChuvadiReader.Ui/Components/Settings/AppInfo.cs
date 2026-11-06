using System.Reflection;

namespace ChuvadiReader.Ui.Components.Settings;

/// <summary>Version strings for Settings › About and the Settings nav footer.</summary>
public static class AppInfo
{
    /// <summary>A library Chuvadi is built on.</summary>
    public sealed record Library(string Role, string Name, string Version);

    /// <summary>Package versions this build actually loads, read from each library assembly's
    /// informational version, so About can never drift from the csproj pins.</summary>
    public static readonly IReadOnlyList<Library> Libraries = new[]
    {
        new Library("PDF engine", "chuvadi-pdf", VersionOf(typeof(Chuvadi.Pdf.Documents.PdfDocument))),
        new Library("Word", "Chuvadi.Docs", VersionOf(typeof(Chuvadi.Docs.Word.Document))),
        new Library("Excel", "Chuvadi.Sheets", VersionOf(typeof(Chuvadi.Sheets.Excel.Workbook))),
        new Library("Icons", "LiPicons.Blazor", VersionOf(typeof(LiPicons.Blazor.LipiIcon))),
    };

    private static string VersionOf(Type type)
    {
        var asm = type.Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrEmpty(info)
            ? info.Split('+')[0]
            : asm.GetName().Version?.ToString(3) ?? "?";
    }

    /// <summary>"Copyright © 2026 Sthraas Intelligence Pvt Ltd. All rights reserved." — read from this
    /// assembly, so Directory.Build.props stays the only place it is written.</summary>
    public static string Copyright =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>The host app's informational version, e.g. "1.0.0+3f2a…".</summary>
    public static string FullVersion { get; } = ReadVersion();

    /// <summary>The version without the source-revision suffix, e.g. "1.0.0".</summary>
    public static string Version { get; } = FullVersion.Split('+')[0];

    private static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info;
        }

        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
