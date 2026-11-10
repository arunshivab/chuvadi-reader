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

/// <summary>TEST FIXTURE ONLY. Makes certificate-signed PDFs through the app's own
/// <see cref="CertificateSignService"/>, and restricted copies with a raw permissions value.</summary>
public static class TestSigning
{
    public static Task SignWithPfxAsync(string sourcePath, string outputPath, string pfxPath, string pfxPassword, CertificateSignOptions? options = null, CancellationToken ct = default)
        => new CertificateSignService().SignWithPfxAsync(sourcePath, outputPath, pfxPath, pfxPassword, options, ct);

    /// <summary>TEST FIXTURE ONLY: writes a restricted, encrypted copy (sets the init-only
    /// permissions by reflection) so the app's handling of such files can be tested.</summary>
    public static void ProtectRestricted(string source, string output, string openPassword, int mask, string? ownerPassword = null)
    {
        using var doc = PdfDocument.Open(source);
        var enc = Chuvadi.Pdf.IO.EncryptionOptions.Aes256(openPassword, ownerPassword ?? "owner-" + Guid.NewGuid().ToString("N"));
        typeof(Chuvadi.Pdf.IO.EncryptionOptions).GetProperty("Permissions")!.SetValue(enc, mask);
        using var fs = File.Create(output);
        doc.Save(fs, enc);
    }
}
