using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChuvadiReader.Core.Reader;

/// <summary>
/// Reads and writes bench session files (a user-chosen <c>.json</c>) and maintains a small library
/// of reusable desk templates in app-data. Pure serialization + file IO — it owns no bench state;
/// <see cref="BenchService"/> produces and consumes the DTOs.
/// </summary>
public sealed class SessionService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── session files (user-chosen path) ───────────────────────────────────────

    public async Task SaveSessionAsync(string path, BenchSessionDto session, CancellationToken ct = default)
    {
        session.SavedAtUtc = DateTimeOffset.UtcNow.ToString("o");
        var json = JsonSerializer.Serialize(session, Json);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    public async Task<BenchSessionDto> LoadSessionAsync(string path, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<BenchSessionDto>(json, Json)
                  ?? throw new InvalidDataException("The session file is empty or not valid.");
        return dto;
    }

    // ── desk templates (app-data library) ──────────────────────────────────────

    private static string TemplatesPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChuvadiReader");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "desk-templates.json");
        }
    }

    public async Task<List<DeskTemplateDto>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var path = TemplatesPath;
        if (!File.Exists(path))
        {
            return new List<DeskTemplateDto>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<DeskTemplateDto>>(json, Json) ?? new List<DeskTemplateDto>();
        }
        catch
        {
            // A corrupt library shouldn't break the bench; start fresh.
            return new List<DeskTemplateDto>();
        }
    }

    /// <summary>Adds or replaces a template (matched by name, case-insensitive) and persists.</summary>
    public async Task SaveTemplateAsync(DeskTemplateDto template, CancellationToken ct = default)
    {
        var list = await ListTemplatesAsync(ct).ConfigureAwait(false);
        list.RemoveAll(t => string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(template);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        await PersistTemplatesAsync(list, ct).ConfigureAwait(false);
    }

    public async Task DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        var list = await ListTemplatesAsync(ct).ConfigureAwait(false);
        if (list.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            await PersistTemplatesAsync(list, ct).ConfigureAwait(false);
        }
    }

    private static async Task PersistTemplatesAsync(List<DeskTemplateDto> list, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(list, Json);
        await File.WriteAllTextAsync(TemplatesPath, json, ct).ConfigureAwait(false);
    }
}
