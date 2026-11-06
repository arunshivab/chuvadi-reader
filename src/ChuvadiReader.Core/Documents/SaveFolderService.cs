using ChuvadiReader.Core.Storage;
using ChuvadiReader.Platform;

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
    private readonly IAppPaths? _paths;

    /// <param name="paths">Host folders. The default save folder is "Chuvadi Documents" under the host's
    /// <see cref="IAppPaths.DefaultSaveFolder"/> (desktop: Documents, as before); a host without a visible
    /// file system (Web) gets it under its working folder, delivered by write-back.</param>
    public SaveFolderService(IAppStorage storage, IAppPaths? paths = null)
    {
        _storage = storage;
        _paths = paths;
        BaseFolder = DefaultFolder;
    }

    public string BaseFolder { get; private set; }

    public bool DateFolders { get; private set; }

    public bool MonthFolders { get; private set; }

    public event Action? Changed;

    /// <summary>The default save folder for this host.</summary>
    public string DefaultFolder => Path.Combine(
        _paths is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : _paths.DefaultSaveFolder ?? ScratchFiles.Folder(_paths),
        "Chuvadi Documents");

    public async Task HydrateAsync()
    {
        var custom = await _storage.GetAsync(FolderKey);
        BaseFolder = string.IsNullOrWhiteSpace(custom) ? DefaultFolder : custom!;
        DateFolders = string.Equals(await _storage.GetAsync(DateKey), "on", StringComparison.OrdinalIgnoreCase);
        MonthFolders = string.Equals(await _storage.GetAsync(MonthKey), "on", StringComparison.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    public async Task SetBaseFolderAsync(string? path)
    {
        BaseFolder = string.IsNullOrWhiteSpace(path) ? DefaultFolder : path!;
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
