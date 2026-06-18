namespace ChuvadiReader.Core.Storage;

/// <summary>
/// Local key/value persistence. Implemented per host: on Windows it writes a
/// JSON file under %AppData%\Chuvadi. Never touches the network — this is the
/// only place app state is stored, and it stays on the device.
/// </summary>
public interface IAppStorage
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);
}
