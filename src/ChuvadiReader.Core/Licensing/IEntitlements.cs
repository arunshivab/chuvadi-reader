namespace ChuvadiReader.Core.Licensing;

/// <summary>
/// Capability gate for features that may later be restricted to certain licence tiers
/// (e.g. Enterprise). This is a plumbing seam, not an enforcement mechanism: today every
/// capability is on. When real licensing arrives, swap the registered implementation for one
/// that reads a licence — no consumer code changes, because every consumer asks this interface
/// rather than assuming the capability.
/// </summary>
public interface IEntitlements
{
    /// <summary>Whether the Redact destination is available to this user.</summary>
    bool CanRedact { get; }
}

/// <summary>Default: everything enabled. The single place to replace when licensing is added.</summary>
public sealed class DefaultEntitlements : IEntitlements
{
    public bool CanRedact => true;
}
