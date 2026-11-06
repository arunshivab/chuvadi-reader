using System.Text.RegularExpressions;
using ChuvadiReader.Core.Reader;
using ChuvadiReader.Core.Storage;
using ChuvadiReader.Hosting;
using ChuvadiReader.Platform;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChuvadiReader.Hosting.Tests;

public sealed class HostingTests
{
    private sealed class MemoryStorage : IAppStorage
    {
        private readonly Dictionary<string, string> _d = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_d.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _d[key] = value;
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Build(AppStateLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddChuvadiReader(lifetime);
        services.AddSingleton<IAppStorage, MemoryStorage>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Theory]
    [InlineData(AppStateLifetime.Process)]
    [InlineData(AppStateLifetime.PerConnection)]
    public void Every_registered_service_resolves(AppStateLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddChuvadiReader(lifetime);
        using var sp = Build(lifetime);
        using var scope = sp.CreateScope();
        foreach (var d in services)
        {
            Assert.NotNull(scope.ServiceProvider.GetService(d.ServiceType));
        }
    }

    [Fact]
    public void PerConnection_gives_each_connection_its_own_state()
    {
        using var sp = Build(AppStateLifetime.PerConnection);
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.NotSame(a.ServiceProvider.GetRequiredService<TabsService>(), b.ServiceProvider.GetRequiredService<TabsService>());
        Assert.Same(a.ServiceProvider.GetRequiredService<TabsService>(), a.ServiceProvider.GetRequiredService<TabsService>());
    }

    [Fact]
    public void Host_registration_after_AddChuvadiReader_wins()
    {
        var services = new ServiceCollection();
        services.AddChuvadiReader(AppStateLifetime.Process);
        services.AddSingleton<IFileWriteBack, TestWriteBack>();
        services.AddSingleton<IAppStorage, MemoryStorage>();
        using var sp = services.BuildServiceProvider();
        Assert.IsType<TestWriteBack>(sp.GetRequiredService<IFileWriteBack>());
    }

    private sealed class TestWriteBack : IFileWriteBack
    {
        public bool IsNeeded => true;

        public Task<WriteBackResult> CommitAsync(string localPath, CancellationToken ct = default) =>
            Task.FromResult(new WriteBackResult(WriteBackOutcome.Done));
    }

    /// <summary>Drift guard: every Core service the Windows host registers is registered by the kit too.</summary>
    [Fact]
    public void Kit_registers_everything_the_Windows_host_registers()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WindowsApp.xaml.cs.txt"));
        var windows = Regex.Matches(text, @"AddSingleton<([\w\.]+)(?:,\s*[\w\.]+)?>\(")
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .ToHashSet();

        // Host-only seams the Windows host implements itself (not part of the shared kit's Core list).
        string[] hostSeams = ["IAppStorage", "IUiScale", "IExternalLinks", "IPrintService", "IFilePicker", "IImageClipboard", "IClipboard", "IPlatformInfo"];

        var services = new ServiceCollection();
        services.AddChuvadiReader(AppStateLifetime.Process);
        var kit = services.Select(d => d.ServiceType.Name).ToHashSet();

        var missing = windows.Except(hostSeams).Except(kit).ToList();
        Assert.True(missing.Count == 0, "Registered by Windows but not by the kit: " + string.Join(", ", missing));
    }

    /// <summary>D-48: restoring the saved tab set never shows the same file twice.</summary>
    [Fact]
    public async Task Restore_never_duplicates_a_tab()
    {
        var services = new ServiceCollection();
        services.AddChuvadiReader(AppStateLifetime.Process);
        var storage = new MemoryStorage();
        await storage.SetAsync("tabs", """
            [{"Path":"/nowhere/a.pdf","FileName":"a.pdf"},{"Path":"/NOWHERE/A.pdf","FileName":"A.pdf"},{"Path":"/nowhere/b.pdf","FileName":"b.pdf"}]
            """);
        services.AddSingleton<IAppStorage>(storage);
        using var sp = services.BuildServiceProvider();
        var tabs = sp.GetRequiredService<TabsService>();

        await tabs.RestoreAsync();

        Assert.Equal(["a.pdf", "b.pdf"], tabs.Tabs.Select(t => t.FileName));
    }

    [Theory]
    [InlineData("Ctrl P", "⌘P")]
    [InlineData("Ctrl Shift S", "⇧⌘S")]
    [InlineData("Ctrl Alt W", "⌥⌘W")]
    [InlineData("Ctrl 1 … 9", "⌘1 … 9")]
    [InlineData("F11", "F11")]
    public void Mac_key_labels(string canonical, string expected) =>
        Assert.Equal(expected, new MacKeyLabels().Format(canonical));

    [Theory]
    [InlineData(PlatformKind.Windows, FormFactor.Desktop, ShellLayout.Desktop)]
    [InlineData(PlatformKind.MacOS, FormFactor.Desktop, ShellLayout.Desktop)]
    [InlineData(PlatformKind.Linux, FormFactor.Desktop, ShellLayout.Desktop)]
    [InlineData(PlatformKind.Android, FormFactor.Tablet, ShellLayout.Tablet)]
    [InlineData(PlatformKind.Android, FormFactor.Phone, ShellLayout.Phone)]
    [InlineData(PlatformKind.Web, FormFactor.Desktop, ShellLayout.Portal)]
    [InlineData(PlatformKind.Web, FormFactor.Phone, ShellLayout.Phone)]
    [InlineData(PlatformKind.Web, FormFactor.Tablet, ShellLayout.Tablet)]
    public void Shell_layout_derivation(PlatformKind kind, FormFactor ff, ShellLayout expected) =>
        Assert.Equal(expected, ShellLayouts.From(kind, ff));

    private static (ShellService Shell, IAppCommands Commands) ShellAndCommands()
    {
        var services = new ServiceCollection();
        services.AddChuvadiReader(AppStateLifetime.Process);
        services.AddSingleton<IAppStorage, MemoryStorage>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ShellService>(), sp.GetRequiredService<IAppCommands>());
    }

    [Fact]
    public void App_commands_come_from_the_shell_registry()
    {
        var (shell, cmds) = ShellAndCommands();
        Assert.IsType<ShellAppCommands>(cmds);
        var changed = 0;
        cmds.Changed += () => changed++;

        shell.RegisterCommands(this, [
            new ShellCommand("doc.print", "Print", "Document", "print", "Ctrl P", () => Task.CompletedTask),
            new ShellCommand("doc.reveal", "Show in folder", "Document", "folder", null, () => Task.CompletedTask, "Coming soon"),
        ]);

        Assert.Equal(1, changed);
        var print = Assert.Single(cmds.Commands, c => c.Id == "doc.print");
        Assert.Equal(("Print", "Document", "Ctrl P", true), (print.Label, print.Group, print.Keys, print.IsEnabled));
        var reveal = Assert.Single(cmds.Commands, c => c.Id == "doc.reveal");
        Assert.False(reveal.IsEnabled);
        Assert.Equal("Coming soon", reveal.DisabledReason);

        shell.UnregisterCommands(this);
        Assert.Empty(cmds.Commands);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Document_menu_keeps_order_and_marks_separators()
    {
        var (shell, cmds) = ShellAndCommands();
        shell.SetDocumentMenu([
            new ShellCommand("doc.props", "Properties", "Document", "info", null, () => Task.CompletedTask),
            new ShellCommand("sep.1", "", "", "", null, () => Task.CompletedTask),
            new ShellCommand("doc.close", "Close", "Document", "close", "Ctrl W", () => Task.CompletedTask),
        ]);

        Assert.Equal(["doc.props", "sep.1", "doc.close"], cmds.DocumentMenu.Select(c => c.Id));
        Assert.True(cmds.DocumentMenu[1].IsSeparator);
        Assert.False(cmds.DocumentMenu[0].IsSeparator);
    }

    [Fact]
    public async Task Invoke_runs_enabled_commands_only()
    {
        var (shell, cmds) = ShellAndCommands();
        var ran = new List<string>();
        shell.RegisterCommands(this, [
            new ShellCommand("a", "A", "File", "x", null, () => { ran.Add("a"); return Task.CompletedTask; }),
            new ShellCommand("b", "B", "File", "x", null, () => { ran.Add("b"); return Task.CompletedTask; }, "not now"),
        ]);
        shell.SetDocumentMenu([new ShellCommand("sep.1", "", "", "", null, () => { ran.Add("sep"); return Task.CompletedTask; })]);

        Assert.True(await cmds.InvokeAsync("a"));
        Assert.False(await cmds.InvokeAsync("b"));
        Assert.False(await cmds.InvokeAsync("sep.1"));
        Assert.False(await cmds.InvokeAsync("missing"));
        Assert.Equal(["a"], ran);
    }
}
