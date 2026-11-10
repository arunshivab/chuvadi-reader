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
/// Certificate signing (Protect › Sign with certificate): signs a copy of a PDF with the
/// certificate and private key in a .pfx/.p12, through chuvadi-pdf's <c>PdfSigner.Sign</c>
/// (working since 3.31.2) for a file with no signature yet and <c>PdfCounterSigner.AddSignature</c>
/// for a file that is already signed (the library's own rule: an added signature is appended, so
/// the earlier ones stay valid). The source is never written to. The .pfx is read with .NET's
/// own PKCS#12 loader, and the key signs through .NET's RSA / ECDSA.
/// </summary>
public sealed class CertificateSignService
{
    /// <summary>True since chuvadi-pdf 3.31.2.</summary>
    public static bool IsAvailable => true;

    /// <summary>Signatures made here are invisible for now (listed in the Signatures card, not
    /// drawn on the page). The library can draw one since 3.32.0; the app's placement design is
    /// with Arun before it is built.</summary>
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

    /// <summary>Signs <paramref name="sourcePath"/> with <paramref name="certificate"/> (which must
    /// hold its private key) and writes the signed copy to <paramref name="outputPath"/>. The copy
    /// is verified before it is kept; a copy that fails verification is not written.</summary>
    public Task SignAsync(
        string sourcePath,
        string outputPath,
        X509Certificate2 certificate,
        CertificateSignOptions? options = null,
        IEnumerable<X509Certificate2>? chain = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        PasswordService.GuardPaths(sourcePath, outputPath);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("This certificate has no private key, so it can't sign.");
        }

        options ??= new CertificateSignOptions();
        var chainDer = (chain ?? Array.Empty<X509Certificate2>())
            .Where(c => !string.Equals(c.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.RawData)
            .ToList();

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (PdfDocument.RequiresPassword(sourcePath))
            {
                throw new InvalidOperationException("Remove the file's password before signing it.");
            }

            byte[] source = File.ReadAllBytes(sourcePath);
            using var signer = new BclCmsSigner(certificate);
            byte[] signed;
            string fieldName;
            using (var doc = PdfDocument.Open(new MemoryStream(source, writable: false), leaveOpen: false))
            {
                var existing = SignatureReader.Read(doc.Catalog, doc.Objects);
                fieldName = string.IsNullOrWhiteSpace(options.FieldName) ? NextFieldName(existing) : options.FieldName!.Trim();
                var signOptions = new PdfSigningOptions
                {
                    SignatureFieldName = fieldName,
                    Reason = Blank(options.Reason),
                    Location = Blank(options.Location),
                    ContactInfo = Blank(options.ContactInfo),
                    SigningTime = options.SigningTime ?? DateTimeOffset.UtcNow,
                    ExtraCertificates = chainDer.Select(ChuvadiX509.Decode).ToList(),
                };

                ct.ThrowIfCancellationRequested();

                // The library's rule: the first signature goes through PdfSigner; a file that is
                // already signed gets its next signature appended, so the earlier ones stay valid.
                signed = existing.Count == 0
                    ? PdfSigner.Sign(doc, signer, signOptions)
                    : PdfCounterSigner.AddSignature(source, signer, signOptions);
            }

            VerifyNewSignature(signed, fieldName);

            ct.ThrowIfCancellationRequested();
            PasswordService.WriteAtomically(outputPath, s => s.Write(signed, 0, signed.Length), ct);
        }, ct);
    }

    private static void VerifyNewSignature(byte[] signed, string fieldName)
    {
        using var doc = PdfDocument.Open(new MemoryStream(signed, writable: false), leaveOpen: false);
        var sig = SignatureReader.Read(doc.Catalog, doc.Objects)
            .LastOrDefault(s => string.Equals(s.FieldName, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Signing didn't produce a signature field.");
        var r = sig.Verify(doc, SignatureVerifyOptions.Default);
        if (!r.IntegrityVerified)
        {
            throw new InvalidOperationException("The signed copy failed verification (" + r.Status + "): " + r.Message);
        }
    }

    internal static string NextFieldName(IReadOnlyList<PdfSignature> existing)
    {
        var used = new HashSet<string>(existing.Select(s => s.FieldName ?? string.Empty), StringComparer.Ordinal);
        int n = 1;
        while (used.Contains("Signature" + n)) n++;
        return "Signature" + n;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();


    private static (X509Certificate2 Leaf, List<X509Certificate2> Others) LoadPfx(string pfxPath, string pfxPassword)
    {
        // Mac Catalyst (the MAUI Mac host) reports IsMacOS() == false but has the same keychain limits.
        var flags = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
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

    /// <summary>A chuvadi <see cref="ISigner"/> backed by the .NET key of an
    /// <see cref="X509Certificate2"/> (SHA-256; RSA PKCS#1 v1.5 or ECDSA with a DER signature,
    /// as CMS expects).</summary>
    internal sealed class BclCmsSigner : ISigner, IDisposable
    {
        private const string Sha256WithRsa = "1.2.840.113549.1.1.11";
        private const string EcdsaWithSha256 = "1.2.840.10045.4.3.2";

        private readonly RSA? _rsa;
        private readonly ECDsa? _ecdsa;

        public BclCmsSigner(X509Certificate2 certificate)
        {
            _rsa = certificate.GetRSAPrivateKey();
            _ecdsa = _rsa is null ? certificate.GetECDsaPrivateKey() : null;
            if (_rsa is null && _ecdsa is null)
            {
                throw new NotSupportedException("Only RSA and ECDSA certificates can sign.");
            }

            Certificate = ChuvadiX509.Decode(certificate.RawData);
            // Parameters absent, matching the library's own RsaPkcs1V15Signer / EcdsaCmsSigner.
            SignatureAlgorithm = new AlgorithmIdentifier(new ObjectIdentifier(_rsa is not null ? Sha256WithRsa : EcdsaWithSha256), null!);
        }

        public ChuvadiX509 Certificate { get; }

        public ChuvadiHash HashAlgorithm => ChuvadiHash.Sha256;

        public AlgorithmIdentifier SignatureAlgorithm { get; }

        public byte[] Sign(byte[] dataToSign) =>
            _rsa is not null
                ? _rsa.SignData(dataToSign, BclHash.SHA256, RSASignaturePadding.Pkcs1)
                : _ecdsa!.SignData(dataToSign, BclHash.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        public void Dispose()
        {
            _rsa?.Dispose();
            _ecdsa?.Dispose();
        }
    }
}
