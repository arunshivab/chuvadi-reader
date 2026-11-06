using System.Text.RegularExpressions;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Hosting.Desktop;
using ChuvadiReader.Platform;
using Xunit;

namespace ChuvadiReader.Hosting.Tests;

/// <summary>Guards for the Group B/C edits: write-back at every save, host folders, shared lists.</summary>
public sealed class GroupBcTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chvd-tests-" + Guid.NewGuid().ToString("N"));

    public GroupBcTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "ChuvadiReader.Platforms.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string AppDataFolder => Path.Combine(root, "appdata");
        public string WorkingFolder => Path.Combine(root, "work");
        public string? DefaultSaveFolder => null;
    }

    /// <summary>Every Ui file that asks for a save target delivers at least as many files (F-1 C).</summary>
    [Fact]
    public void Every_save_site_delivers_its_file()
    {
        var ui = Path.Combine(RepoRoot(), "src", "ChuvadiReader.Ui");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ui, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            var saves = Regex.Matches(text, @"Picker\.PickSave\w*Async\(").Count + Regex.Matches(text, @"File\.Replace\(").Count;
            var delivers = Regex.Matches(text, @"DeliverOrThrowAsync\(").Count;
            if (saves > delivers)
            {
                problems.Add($"{Path.GetFileName(file)}: {saves} save site(s), {delivers} delivery call(s)");
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>Window drop accepts exactly what the Open dialog offers (Mac chat F1).</summary>
    [Fact]
    public void Openable_types_match_the_Windows_open_dialog()
    {
        var picker = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ChuvadiReader.Windows", "Platform", "WpfFilePicker.cs"));
        var m = Regex.Match(picker, @"""All supported \(([^)]*)\)\|");
        Assert.True(m.Success, "Open filter not found");
        var dialog = m.Groups[1].Value.Split(';').Select(e => e.Trim().TrimStart('*').ToLowerInvariant()).OrderBy(e => e);
        Assert.Equal(dialog, DocumentFormatExtensions.OpenableExtensions.OrderBy(e => e));
        Assert.False(DocumentFormatExtensions.IsOpenable("old.doc"));
        Assert.False(DocumentFormatExtensions.IsOpenable("old.xls"));
        Assert.True(DocumentFormatExtensions.IsOpenable("Notes.MD"));
    }

    /// <summary>Settings are replaced atomically; no temporary file is left behind (Mac chat F3).</summary>
    [Fact]
    public async Task Settings_write_replaces_the_file()
    {
        var store = new FileAppStorage(_dir);
        await store.SetAsync("theme", "dark");
        await store.SetAsync("scale", "125");
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json.tmp")));

        var again = new FileAppStorage(_dir);
        Assert.Equal("dark", await again.GetAsync("theme"));
        Assert.Equal("125", await again.GetAsync("scale"));
    }

    /// <summary>Desk templates live in the host's app-data folder (Mac chat F2).</summary>
    [Fact]
    public async Task Templates_live_in_the_host_app_data_folder()
    {
        var paths = new Paths(_dir);
        var sessions = new SessionService(paths);
        await sessions.SaveTemplateAsync(new DeskTemplateDto { Name = "Discharge" });
        Assert.True(File.Exists(Path.Combine(paths.AppDataFolder, "desk-templates.json")));
        Assert.Contains(await sessions.ListTemplatesAsync(), t => t.Name == "Discharge");
    }

    [Fact]
    public void Scratch_files_go_to_the_host_working_folder()
    {
        var paths = new Paths(_dir);
        var folder = ScratchFiles.Folder(paths);
        Assert.Equal(paths.WorkingFolder, folder);
        var a = ScratchFiles.NewFile(folder);
        var b = ScratchFiles.NewFile(folder);
        Assert.NotEqual(a, b);
        Assert.Equal(0, new FileInfo(a).Length);
        Assert.Equal(Path.GetTempPath(), ScratchFiles.Folder(null));
    }

    [Fact]
    public void Default_save_folder_follows_the_host()
    {
        var paths = new Paths(_dir);
        var saves = new SaveFolderService(new NullStorage(), paths);
        Assert.Equal(Path.Combine(paths.WorkingFolder, "Chuvadi Documents"), saves.DefaultFolder);

        var desktop = new SaveFolderService(new NullStorage());
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Chuvadi Documents"),
            desktop.DefaultFolder);
    }

    [Theory]
    [InlineData("/home/a/Documents", "/home/a/Documents", "/home/a", "/home/a/Documents")]       // exists: as before
    [InlineData("", "/home/a/Docs", "/home/a", "/home/a/Docs")]                                 // XDG folder not created yet
    [InlineData("", "/home/a", "/home/a", "/home/a/Documents")]                                 // XDG points at home itself
    [InlineData("", "", "/home/a", "/home/a/Documents")]                                        // nothing configured
    [InlineData("", "", "", null)]                                                              // no home folder
    public void Save_folder_falls_back_when_Documents_is_missing(string existing, string unverified, string home, string? expected) =>
        Assert.Equal(expected, DesktopAppPaths.ResolveSaveFolder(existing, unverified, home)?.Replace('\\', '/'));

    private sealed class NullStorage : IAppStorage
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }
}
