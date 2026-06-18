using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// Resolves where Chuvadi saves output files. By default that's a
/// "Chuvadi Documents" folder under the operating system's Documents folder. The
/// user can change the base folder and opt into date and/or month subfolders from
/// Settings; with neither enabled, files land directly in the base folder.
/// </summary>
public sealed class SaveFolderService
{
    private const string FolderKey = "save-folder";
    private const string DateKey = "save-date-folders";
    private const string MonthKey = "save-month-folders";

    private readonly IAppStorage _storage;

    public SaveFolderService(IAppStorage storage) => _storage = storage;

    public string BaseFolder { get; private set; } = DefaultBaseFolder();

    public bool DateFolders { get; private set; }

    public bool MonthFolders { get; private set; }

    public event Action? Changed;

    public static string DefaultBaseFolder()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Chuvadi Documents");

    public async Task HydrateAsync()
    {
        var custom = await _storage.GetAsync(FolderKey);
        BaseFolder = string.IsNullOrWhiteSpace(custom) ? DefaultBaseFolder() : custom!;
        DateFolders = string.Equals(await _storage.GetAsync(DateKey), "on", StringComparison.OrdinalIgnoreCase);
        MonthFolders = string.Equals(await _storage.GetAsync(MonthKey), "on", StringComparison.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    public async Task SetBaseFolderAsync(string? path)
    {
        BaseFolder = string.IsNullOrWhiteSpace(path) ? DefaultBaseFolder() : path!;
        await _storage.SetAsync(FolderKey, BaseFolder);
        Changed?.Invoke();
    }

    public async Task SetDateFoldersAsync(bool on)
    {
        DateFolders = on;
        await _storage.SetAsync(DateKey, on ? "on" : "off");
        Changed?.Invoke();
    }

    public async Task SetMonthFoldersAsync(bool on)
    {
        MonthFolders = on;
        await _storage.SetAsync(MonthKey, on ? "on" : "off");
        Changed?.Invoke();
    }

    /// <summary>The folder a save should default to right now, created if needed.
    /// Month and date nest when both are on (base/yyyy-MM/yyyy-MM-dd).</summary>
    public string ResolveSaveFolder()
    {
        var folder = BaseFolder;
        if (MonthFolders)
        {
            folder = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM"));
        }

        if (DateFolders)
        {
            folder = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd"));
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = BaseFolder;
            try { Directory.CreateDirectory(folder); } catch { /* fall back to whatever the dialog defaults to */ }
        }

        return folder;
    }

    /// <summary>A named subfolder directly under the base folder (e.g. "Export"),
    /// created if missing.</summary>
    public string SubFolder(string name)
    {
        var folder = Path.Combine(BaseFolder, name);
        try { Directory.CreateDirectory(folder); }
        catch { /* best effort */ }
        return folder;
    }
}
