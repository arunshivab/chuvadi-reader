using System.Text.Json;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Core.Documents;

/// <summary>
/// Tags ("categories") on documents, keyed by file path. A document can carry
/// several tags; tags are free text but reused via autocomplete, and each gets a
/// stable colour. Persisted locally as JSON.
/// </summary>
public sealed class TagService
{
    private const string StorageKey = "doc-tags";
    private readonly IAppStorage _storage;
    private Dictionary<string, List<string>> _tags = new(StringComparer.OrdinalIgnoreCase);

    public TagService(IAppStorage storage) => _storage = storage;

    public event Action? Changed;

    public async Task HydrateAsync()
    {
        var json = await _storage.GetAsync(StorageKey);
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (loaded is not null)
                {
                    foreach (var kv in loaded)
                    {
                        map[kv.Key] = kv.Value;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        _tags = map;
        Changed?.Invoke();
    }

    public IReadOnlyList<string> GetTags(string path)
        => _tags.TryGetValue(path, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> AllTags()
        => _tags.Values.SelectMany(v => v)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task AddTagAsync(string path, string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0)
        {
            return;
        }

        if (!_tags.TryGetValue(path, out var list))
        {
            list = new List<string>();
            _tags[path] = list;
        }

        if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(tag);
            await SaveAsync();
            Changed?.Invoke();
        }
    }

    public async Task RemoveTagAsync(string path, string tag)
    {
        if (_tags.TryGetValue(path, out var list) &&
            list.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            if (list.Count == 0)
            {
                _tags.Remove(path);
            }

            await SaveAsync();
            Changed?.Invoke();
        }
    }

    /// <summary>Stable colour bucket (0–7) for a tag, matching the 8-colour palette.</summary>
    public static int ColorIndex(string tag)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in tag.ToLowerInvariant())
            {
                h = (h * 31) + c;
            }

            return Math.Abs(h) % 8;
        }
    }

    private Task SaveAsync() => _storage.SetAsync(StorageKey, JsonSerializer.Serialize(_tags));
}
