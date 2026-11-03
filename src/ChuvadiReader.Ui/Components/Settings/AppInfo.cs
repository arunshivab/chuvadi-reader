using System.Reflection;

namespace ChuvadiReader.Ui.Components.Settings;

/// <summary>Version strings for Settings › About and the Settings nav footer.</summary>
public static class AppInfo
{
    /// <summary>A library Chuvadi is built on.</summary>
    public sealed record Library(string Role, string Name, string Version);

    /// <summary>Package versions this build references (Core / Ui csproj). Kept in one place so
    /// About matches the csproj pins; update together when a library version bumps.</summary>
    public static readonly IReadOnlyList<Library> Libraries = new[]
    {
        new Library("PDF engine", "chuvadi-pdf", "3.30.2"),
        new Library("Word", "Chuvadi.Docs", "1.1.0"),
        new Library("Excel", "Chuvadi.Sheets", "1.1.2"),
        new Library("Icons", "LiPicons.Blazor", "1.1.0"),
    };

    /// <summary>The host app's informational version, e.g. "1.0.0+3f2a…".</summary>
    public static string FullVersion { get; } = ReadVersion();

    /// <summary>The version without the source-revision suffix, e.g. "1.0.0".</summary>
    public static string Version { get; } = FullVersion.Split('+')[0];

    /// <summary>Where the host keeps settings (the Windows host writes %AppData%\Chuvadi).</summary>
    public static string DataFolder { get; } = ReadDataFolder();

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

    private static string ReadDataFolder()
    {
        try
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chuvadi");
        }
        catch
        {
            return "%AppData%\\Chuvadi";
        }
    }
}
