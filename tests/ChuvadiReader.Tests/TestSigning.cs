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

using ChuvadiReader.Core.Documents;

namespace ChuvadiReader.Tests;

/// <summary>TEST FIXTURE ONLY. Makes certificate-signed PDFs so signature verification can be
/// tested. It signs through <see cref="PdfCounterSigner"/> because chuvadi-pdf 3.30.1's
/// <see cref="PdfSigner.Sign"/> fails; the app itself does not sign until the library is fixed.</summary>
public static class TestSigning
{
    public static async Task SignWithPfxAsync(string sourcePath, string outputPath, string pfxPath, string pfxPassword, CertificateSignOptions? options = null, CancellationToken ct = default)
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

    public static Task SignAsync(
        string sourcePath,
        string outputPath,
        X509Certificate2 certificate,
        CertificateSignOptions? options = null,
        IEnumerable<X509Certificate2>? chain = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (Path.GetFullPath(sourcePath) == Path.GetFullPath(outputPath)) throw new InvalidOperationException("Never overwrite the source.");
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
            IReadOnlyList<PdfSignature> existing;
            using (var doc = PdfDocument.Open(new MemoryStream(source, writable: false), leaveOpen: false))
            {
                existing = SignatureReader.Read(doc.Catalog, doc.Objects);
            }

            using var signer = new BclCmsSigner(certificate);
            var signOptions = new PdfSigningOptions
            {
                SignatureFieldName = string.IsNullOrWhiteSpace(options.FieldName) ? NextFieldName(existing) : options.FieldName!.Trim(),
                Reason = Blank(options.Reason),
                Location = Blank(options.Location),
                ContactInfo = Blank(options.ContactInfo),
                SigningTime = options.SigningTime ?? DateTimeOffset.UtcNow,
                ExtraCertificates = chainDer.Select(ChuvadiX509.Decode).ToList(),
            };

            ct.ThrowIfCancellationRequested();
            byte[] signed = PdfCounterSigner.AddSignature(source, signer, signOptions);
            VerifyNewSignature(signed, signOptions.SignatureFieldName!);

            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(outputPath, signed);
        }, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    internal static void VerifyNewSignature(byte[] signed, string fieldName)
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

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static (X509Certificate2 Leaf, List<X509Certificate2> Others) LoadPfx(string pfxPath, string pfxPassword)
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

    internal static CertificateSummary Summarise(X509Certificate2 c)
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

    /// <summary>A chuvadi <see cref="ISigner"/> backed by the BCL key of an
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

    /// <summary>TEST FIXTURE ONLY: writes a restricted, encrypted copy (sets the init-only
    /// permissions by reflection) so the app's handling of such files can be tested.</summary>
    public static void ProtectRestricted(string source, string output, string openPassword, int mask)
    {
        using var doc = PdfDocument.Open(source);
        var enc = Chuvadi.Pdf.IO.EncryptionOptions.Aes256(openPassword, "owner-" + Guid.NewGuid().ToString("N"));
        typeof(Chuvadi.Pdf.IO.EncryptionOptions).GetProperty("Permissions")!.SetValue(enc, mask);
        using var fs = File.Create(output);
        doc.Save(fs, enc);
    }
}
