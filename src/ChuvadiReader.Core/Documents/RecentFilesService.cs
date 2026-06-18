using System.Text.Json;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// Tracks recently opened documents. Persists to <see cref="IAppStorage"/>.
/// On first run (nothing persisted yet) it seeds a small sample so the
/// dashboard has something to show during Phase 0.
/// </summary>
public sealed class RecentFilesService
{
    private const string StorageKey = "recents";
    private readonly IAppStorage _storage;
    private readonly List<RecentItem> _items = new();

    public RecentFilesService(IAppStorage storage) => _storage = storage;

    public IReadOnlyList<RecentItem> Items => _items;

    public int EncryptedCount => _items.Count(i => i.IsEncrypted);

    public event Action? Changed;

    public async Task HydrateAsync(CancellationToken ct = default)
    {
        var json = await _storage.GetAsync(StorageKey, ct).ConfigureAwait(false);
        _items.Clear();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<RecentItem>>(json);
                if (loaded is not null)
                {
                    _items.AddRange(loaded);
                }
            }
            catch (JsonException)
            {
                // Corrupt store: ignore and start clean.
            }
        }

        if (_items.Count == 0)
        {
            SeedSample();
        }

        Changed?.Invoke();
    }

    public async Task TouchAsync(RecentItem item, CancellationToken ct = default)
    {
        _items.RemoveAll(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, item);
        if (_items.Count > 20)
        {
            _items.RemoveRange(20, _items.Count - 20);
        }

        await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(_items), ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void SeedSample()
    {
        var now = DateTimeOffset.Now;
        _items.AddRange(new[]
        {
            new RecentItem("patient_chart.pdf", "~/Documents/Chuvadi/patient_chart.pdf", DocumentFormat.Pdf, now.AddMinutes(-9), false),
            new RecentItem("consent_form.pdf", "~/Documents/Chuvadi/consent_form.pdf", DocumentFormat.Pdf, now.AddHours(-1), true),
            new RecentItem("q2_revenue.xlsx", "~/Documents/Chuvadi/q2_revenue.xlsx", DocumentFormat.Xlsx, now.AddHours(-5), false),
            new RecentItem("census_report.docx", "~/Documents/Chuvadi/census_report.docx", DocumentFormat.Docx, now.AddDays(-1), false),
            new RecentItem("aerb_submission.pdf", "~/Documents/Chuvadi/aerb_submission.pdf", DocumentFormat.Pdf, now.AddDays(-2), false),
        });
    }
}
