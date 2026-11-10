using System.Security.Cryptography;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;

namespace ChuvadiReader.Core.Documents;

/// <summary>What a password-protected copy needs. Permissions apply to someone who opens the
/// copy with the open password; the permissions password lifts them.</summary>
public sealed record PasswordProtectOptions
{
    /// <summary>Password needed to open the copy (the PDF "user" password). Required.</summary>
    public required string OpenPassword { get; init; }

    /// <summary>Password that lifts the restrictions (the PDF "owner" password). When null a
    /// random one is generated and discarded, so the restrictions can't be lifted later.</summary>
    public string? PermissionsPassword { get; init; }

    public bool AllowPrint { get; init; } = true;

    /// <summary>Copying/extracting text and images. Accessibility extraction stays allowed.</summary>
    public bool AllowCopy { get; init; } = true;

    /// <summary>Changing the document: content edits and page assembly (insert/rotate/delete).</summary>
    public bool AllowModify { get; init; } = true;

    /// <summary>Adding or changing comments/annotations.</summary>
    public bool AllowAnnotate { get; init; } = true;

    /// <summary>Filling existing form fields (including signing signature fields).</summary>
    public bool AllowFillForms { get; init; } = true;
}

/// <summary>Encryption state of a file, for the Password card.</summary>
public sealed record PdfProtectionInfo(
    bool IsEncrypted,
    string? Algorithm,
    bool AllowPrint,
    bool AllowCopy,
    bool AllowModify,
    bool AllowAnnotate,
    bool AllowFillForms);

/// <summary>Removing the password was refused: the file forbids changes and the password given
/// was not the owner password. The card then asks for the owner password.</summary>
public sealed class PasswordRemovalRefusedException(string message) : InvalidOperationException(message);

/// <summary>Strength hint for the password meter (0 = empty … 4 = strong).</summary>
public sealed record PasswordStrength(int Score, string Label);

/// <summary>
/// Writes AES-256 (V5/R6) encrypted copies of a PDF and decrypted copies of a protected one,
/// with chuvadi-pdf's <see cref="PdfDocument.Save(Stream, EncryptionOptions?)"/>. Never
/// writes over the source: the output path is chosen by the caller and must differ from it,
/// and the copy is written to a temporary file next to the output first, so a failure never
/// leaves a half-written file behind.
/// </summary>
public sealed class PasswordService
{
    /// <summary>Shown when removal is refused: the file forbids changes and the password given
    /// is the open (user) password, not the owner password.</summary>
    public const string RemoveRefusedMessage =
        "This file's permissions don't allow changes, so the password that opens it can't remove its protection. "
        + "Enter the permissions password to remove it.";

    /// <summary>True since chuvadi-pdf 3.32.0: a protected copy can limit printing, copying and changes.</summary>
    public static bool PermissionsAvailable => true;

    // PDF 32000-1 §7.6.3.2 Table 22 — /P bits (1-based bit n = 1 << (n-1)).
    public const int BitPrint = 1 << 2;          // bit 3
    public const int BitModify = 1 << 3;         // bit 4
    public const int BitCopy = 1 << 4;           // bit 5
    public const int BitAnnotate = 1 << 5;       // bit 6
    public const int BitFillForms = 1 << 8;      // bit 9
    public const int BitAssemble = 1 << 10;      // bit 11
    public const int BitPrintHighQuality = 1 << 11; // bit 12

    /// <summary>True when the file can't be opened without a password.</summary>
    public bool RequiresPassword(string path) => PdfDocument.RequiresPassword(path);

    /// <summary>Reads a file's encryption state (opening it with <paramref name="password"/> when needed).</summary>
    public PdfProtectionInfo GetProtection(string path, string? password = null)
    {
        using var doc = Open(path, password);
        var e = doc.Encryption;
        return e is null
            ? new PdfProtectionInfo(false, null, true, true, true, true, true)
            : new PdfProtectionInfo(true, DescribeAlgorithm(e), e.AllowPrint, e.AllowCopy, e.AllowModify, e.AllowAnnotate, e.AllowFillForms);
    }

    /// <summary>Writes an AES-256 encrypted copy of <paramref name="sourcePath"/> to <paramref name="outputPath"/>.</summary>
    /// <param name="sourcePassword">Needed when the source is itself protected.</param>
    public Task ProtectAsync(
        string sourcePath,
        string outputPath,
        PasswordProtectOptions options,
        string? sourcePassword = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.OpenPassword))
        {
            throw new ArgumentException("An open password is required.", nameof(options));
        }

        GuardPaths(sourcePath, outputPath);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var doc = Open(sourcePath, sourcePassword);
            string owner = string.IsNullOrEmpty(options.PermissionsPassword) ? RandomPassword() : options.PermissionsPassword!;
            var enc = EncryptionOptions.Aes256(options.OpenPassword, owner).WithPermissions(BuildPermissions(options));
            WriteAtomically(outputPath, s => doc.Save(s, enc), ct);
        }, ct);
    }

    /// <summary>Writes a decrypted copy. <paramref name="password"/> must open the file.
    /// Removing protection is a change, so it needs the right to change the file.</summary>
    /// <remarks>The library decides what the opener may do (chuvadi-pdf 3.32.0
    /// <c>EncryptionInfo.EffectiveFlags</c>): everything for the owner password; for the open
    /// (user) password, what the file's permissions allow. So the owner can always remove the
    /// protection, and someone holding only the open password can when the file allows changes —
    /// otherwise it is refused with <see cref="RemoveRefusedMessage"/> (decision 2026-11-06,
    /// option C, completed now that the library says which password opened the file).</remarks>
    public Task RemovePasswordAsync(string sourcePath, string password, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        GuardPaths(sourcePath, outputPath);
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var doc = Open(sourcePath, password);
            var e = doc.Encryption ?? throw new InvalidOperationException("This file isn't password-protected.");
            if (!e.EffectiveFlags.HasFlag(PdfPermissions.ModifyContents))
            {
                throw new PasswordRemovalRefusedException(RemoveRefusedMessage);
            }

            WriteAtomically(outputPath, s => doc.Save(s, null), ct);
        }, ct);
    }

    /// <summary>What someone who opens the copy with the open password may do. Reading for
    /// accessibility (screen readers) always stays allowed.</summary>
    public static PdfPermissions BuildPermissions(PasswordProtectOptions o)
    {
        var p = PdfPermissions.All;
        if (!o.AllowPrint) p &= ~(PdfPermissions.Print | PdfPermissions.PrintHighQuality);
        if (!o.AllowCopy) p &= ~PdfPermissions.CopyContents;
        if (!o.AllowModify) p &= ~(PdfPermissions.ModifyContents | PdfPermissions.Assemble);
        if (!o.AllowAnnotate) p &= ~PdfPermissions.ModifyAnnotations;
        if (!o.AllowFillForms) p &= ~PdfPermissions.FillForms;
        return p;
    }

    /// <summary>The /P value the library writes for <paramref name="o"/>.</summary>
    public static int BuildPermissionMask(PasswordProtectOptions o) =>
        EncryptionOptions.PermissionMask(BuildPermissions(o));

    /// <summary>A simple strength hint: length plus character variety.</summary>
    public static PasswordStrength RatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password)) return new PasswordStrength(0, "Empty");
        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        int score = password.Length switch { < 6 => 1, < 10 => 2, < 14 => 3, _ => 4 };
        if (classes <= 1 && score > 1) score--;
        if (classes >= 3 && score < 4 && password.Length >= 8) score++;
        string label = score switch { 1 => "Weak", 2 => "Fair", 3 => "Good", _ => "Strong" };
        return new PasswordStrength(score, $"{label} · {password.Length} characters");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static PdfDocument Open(string path, string? password) =>
        string.IsNullOrEmpty(password) ? PdfDocument.Open(path) : PdfDocument.Open(path, password);

    internal static void GuardPaths(string sourcePath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), cmp))
        {
            throw new InvalidOperationException("Choose a different file name — the original is never overwritten.");
        }
    }

    internal static void WriteAtomically(string outputPath, Action<Stream> write, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        var temp = Path.Combine(dir, "." + Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(fs);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temp, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static string RandomPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DescribeAlgorithm(EncryptionInfo e) => e.Algorithm.ToString() switch
    {
        "Aes_256" => "AES-256",
        "Aes_128" => "AES-128",
        "Rc4_128" => "RC4 128-bit",
        "Rc4_40" => "RC4 40-bit",
        var other => other,
    };
}
