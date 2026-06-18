using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChuvadiReader.Core.Storage;

namespace ChuvadiReader.Windows.Platform;

/// <summary>
/// Stores app state as a single JSON file under %AppData%\Chuvadi. No network,
/// no registry — just a file the user owns. Pure BCL (System.Text.Json).
/// </summary>
public sealed class FileAppStorage : IAppStorage
{
    private readonly string _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string> _cache = new();
    private bool _loaded;

    public FileAppStorage(string directory)
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "settings.json");
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return _cache.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>Synchronous read for startup decisions made before the UI loop runs.</summary>
    public string? GetSync(string key)
    {
        if (!_loaded)
        {
            _gate.Wait();
            try
            {
                if (!_loaded)
                {
                    if (File.Exists(_file))
                    {
                        try
                        {
                            _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file)) ?? new();
                        }
                        catch (JsonException)
                        {
                            _cache = new();
                        }
                    }

                    _loaded = true;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return _cache.TryGetValue(key, out var v) ? v : null;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cache[key] = value;
            var json = JsonSerializer.Serialize(_cache);
            await File.WriteAllTextAsync(_file, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_file))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_file, ct).ConfigureAwait(false);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
                catch (JsonException)
                {
                    _cache = new();
                }
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
