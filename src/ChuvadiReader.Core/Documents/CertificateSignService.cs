using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Signatures;
using Chuvadi.Pdf.Signatures.Signing;
using Chuvadi.Pdf.Signatures.Verification;
using BclHash = System.Security.Cryptography.HashAlgorithmName;
using ChuvadiHash = Chuvadi.Cryptography.Hashing.HashAlgorithmName;
using ChuvadiX509 = Chuvadi.Cryptography.X509.X509Certificate;

namespace ChuvadiReader.Core.Documents;

/// <summary>Signature dictionary details for <see cref="CertificateSignService"/>.</summary>
public sealed record CertificateSignOptions
{
    public string? Reason { get; init; }

    public string? Location { get; init; }

    public string? ContactInfo { get; init; }

    /// <summary>Signature field name; default "Signature{n}" (first free number).</summary>
    public string? FieldName { get; init; }

    /// <summary>Signing time; default now.</summary>
    public DateTimeOffset? SigningTime { get; init; }
}

/// <summary>What the Sign card shows about a certificate before signing.</summary>
public sealed record CertificateSummary(
    string SubjectName,
    string IssuerName,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool HasPrivateKey,
    string KeyAlgorithm,
    string Thumbprint)
{
    public bool IsCurrentlyValid => DateTimeOffset.UtcNow >= NotBefore && DateTimeOffset.UtcNow <= NotAfter;
}

/// <summary>
/// Certificate signing (Protect › Sign with certificate). Reading a .pfx/.p12 works; signing
/// itself is disabled until chuvadi-pdf's <c>PdfSigner.Sign</c> works (it fails in 3.30.1 with
/// "Could not locate /Contents value in the signed PDF output"). The app does not work around
/// the library, so the button is greyed out with <see cref="WaitingReason"/>.
/// </summary>
public sealed class CertificateSignService
{
    /// <summary>Shown on the greyed-out Sign with certificate button.</summary>
    public const string WaitingReason = "Waiting on the PDF library — certificate signing fails in chuvadi-pdf 3.30.1";

    /// <summary>False until the library's signing call works.</summary>
    public static bool IsAvailable => false;

    /// <summary>False in 3.30: the incremental signing path ignores
    /// <see cref="PdfSigningOptions.Appearance"/>, so signatures are invisible (shown in the
    /// Signatures card, not on the page).</summary>
    public bool SupportsVisibleAppearance => false;

    /// <summary>Opens a .pfx/.p12 and describes its signing certificate. Throws
    /// <see cref="CryptographicException"/> on a wrong password or unreadable file.</summary>
    public CertificateSummary InspectPfx(string pfxPath, string pfxPassword)
    {
        var (leaf, others) = LoadPfx(pfxPath, pfxPassword);
        try
        {
            return Summarise(leaf);
        }
        finally
        {
            leaf.Dispose();
            foreach (var c in others) c.Dispose();
        }
    }

    /// <summary>Signs <paramref name="sourcePath"/> with the certificate in a .pfx/.p12 and writes the
    /// signed copy to <paramref name="outputPath"/> (never over the source).</summary>
    public async Task SignWithPfxAsync(
        string sourcePath,
        string outputPath,
        string pfxPath,
        string pfxPassword,
        CertificateSignOptions? options = null,
        CancellationToken ct = default)
    {
        var (leaf, others) = LoadPfx(pfxPath, pfxPassword);
        try
        {
            await SignAsync(sourcePath, outputPath, leaf, options, others, ct).ConfigureAwait(false);
        }
        finally
        {
            leaf.Dispose();
            foreach (var c in others) c.Dispose();
        }
    }

    /// <summary>Signing is waiting on the PDF library (chuvadi-pdf 3.30.1): <c>PdfSigner.Sign</c>
    /// fails with "Could not locate /Contents value in the signed PDF output". The app does not
    /// work around a library gap, so this throws until the library is fixed.</summary>
    public Task SignAsync(
        string sourcePath,
        string outputPath,
        X509Certificate2 certificate,
        CertificateSignOptions? options = null,
        IEnumerable<X509Certificate2>? chain = null,
        CancellationToken ct = default)
        => throw new NotSupportedException(WaitingReason);


    private static (X509Certificate2 Leaf, List<X509Certificate2> Others) LoadPfx(string pfxPath, string pfxPassword)
    {
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        var all = X509CertificateLoader.LoadPkcs12CollectionFromFile(pfxPath, pfxPassword, flags);
        var leaf = all.FirstOrDefault(c => c.HasPrivateKey);
        if (leaf is null)
        {
            foreach (var c in all) c.Dispose();
            throw new InvalidOperationException("The certificate file has no private key, so it can't sign.");
        }

        return (leaf, all.Where(c => !ReferenceEquals(c, leaf)).ToList());
    }

    private static CertificateSummary Summarise(X509Certificate2 c)
    {
        string key = c.GetRSAPublicKey() is { } rsa ? $"RSA {rsa.KeySize}"
            : c.GetECDsaPublicKey() is { } ec ? $"ECDSA {ec.KeySize}"
            : c.PublicKey.Oid.FriendlyName ?? c.PublicKey.Oid.Value ?? "unknown";
        return new CertificateSummary(
            c.GetNameInfo(X509NameType.SimpleName, false),
            c.GetNameInfo(X509NameType.SimpleName, true),
            new DateTimeOffset(c.NotBefore.ToUniversalTime(), TimeSpan.Zero),
            new DateTimeOffset(c.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            c.HasPrivateKey,
            key,
            c.Thumbprint);
    }

}
