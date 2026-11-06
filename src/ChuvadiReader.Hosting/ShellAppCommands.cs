using ChuvadiReader.Core.Reader;
using ChuvadiReader.Platform;

namespace ChuvadiReader.Hosting;

/// <summary>
/// <see cref="IAppCommands"/> filled from <see cref="ShellService"/> — the same registry the Ctrl K bar
/// and the Document ▾ menu read — so a native menu bar shows exactly what the Ui offers, with no Ui hook.
/// </summary>
public sealed class ShellAppCommands : IAppCommands, IDisposable
{
    private const string SeparatorPrefix = "sep.";
    private readonly ShellService _shell;

    public ShellAppCommands(ShellService shell)
    {
        _shell = shell;
        _shell.Changed += OnShellChanged;
    }

    public IReadOnlyList<AppCommandInfo> Commands =>
        _shell.Commands.Where(c => !IsSeparator(c)).Select(ToInfo).ToList();

    public IReadOnlyList<AppCommandInfo> DocumentMenu =>
        _shell.DocumentMenu.Select(ToInfo).ToList();

    public event Action? Changed;

    public async Task<bool> InvokeAsync(string id)
    {
        var command = _shell.DocumentMenu.FirstOrDefault(c => c.Id == id)
                      ?? _shell.Commands.FirstOrDefault(c => c.Id == id);
        if (command is null || IsSeparator(command) || !command.IsEnabled)
        {
            return false;
        }

        await command.Run();
        return true;
    }

    public void Dispose() => _shell.Changed -= OnShellChanged;

    private void OnShellChanged() => Changed?.Invoke();

    private static bool IsSeparator(ShellCommand c) =>
        c.Id.StartsWith(SeparatorPrefix, StringComparison.Ordinal) && c.Label.Length == 0;

    private static AppCommandInfo ToInfo(ShellCommand c) => IsSeparator(c)
        ? new AppCommandInfo(c.Id, string.Empty, string.Empty, null, false, null, IsSeparator: true)
        : new AppCommandInfo(c.Id, c.Label, c.Group, c.Keys, c.IsEnabled, c.DisabledReason);
}
