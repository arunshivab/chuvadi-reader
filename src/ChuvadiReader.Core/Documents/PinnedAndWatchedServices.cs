using System.Text.Json;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Documents;

/// <summary>Documents and folders the user has pinned for quick access.</summary>
public sealed class PinnedService
{
    private const string StorageKey = "pinned";
    private readonly IAppStorage _storage;
    private readonly List<PinnedItem> _items = new();

    public PinnedService(IAppStorage storage) => _storage = storage;

    public IReadOnlyList<PinnedItem> Items => _items;

    public event Action? Changed;

    public async Task HydrateAsync(CancellationToken ct = default)
    {
        var json = await _storage.GetAsync(StorageKey, ct).ConfigureAwait(false);
        _items.Clear();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<PinnedItem>>(json);
                if (loaded is not null)
                {
                    _items.AddRange(loaded);
                }
            }
            catch (JsonException)
            {
            }
        }

        if (_items.Count == 0)
        {
            _items.Add(new PinnedItem("aerb_submission.pdf", "~/Documents/Chuvadi/aerb_submission.pdf", false, DocumentFormat.Pdf));
            _items.Add(new PinnedItem("IMCHRC / PET-CT", "~/Documents/Chuvadi/IMCHRC", true, null));
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// A folder the app scans for documents so they appear without manual opening.
/// Phase 0 seeds a sample; the real implementation will back this with a
/// FileSystemWatcher in the host.
/// </summary>
public sealed class WatchedFolderService
{
    private const string StorageKey = "watched-folder";
    private readonly IAppStorage _storage;

    public WatchedFolderService(IAppStorage storage) => _storage = storage;

    public string? FolderPath { get; private set; }

    public int DocumentCount { get; private set; }

    public DateTimeOffset? LastScanned { get; private set; }

    public event Action? Changed;

    public async Task HydrateAsync(CancellationToken ct = default)
    {
        FolderPath = await _storage.GetAsync(StorageKey, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(FolderPath))
        {
            FolderPath = "~/Documents/Chuvadi";
            DocumentCount = 8;
            LastScanned = DateTimeOffset.Now.AddMinutes(-2);
        }

        Changed?.Invoke();
    }

    public async Task SetFolderAsync(string path, CancellationToken ct = default)
    {
        FolderPath = path;
        await _storage.SetAsync(StorageKey, path, ct).ConfigureAwait(false);
        Changed?.Invoke();
    }
}
