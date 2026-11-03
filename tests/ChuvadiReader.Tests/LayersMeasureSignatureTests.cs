using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Graphics;
using ChuvadiReader.Core.Documents;
using ChuvadiReader.Core.Reader;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>Layers (view-only optional-content toggles), Measure (1:1 and file scale),
/// signature verification and certificate signing, and the print page-range parser.</summary>
public class LayersMeasureSignatureTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Writes a PDF from raw object bodies (1-based in order), computing the xref —
    /// the authoring API has no optional-content or viewport support, so these are hand-built.</summary>
    internal static string RawPdf(params string[] objects)
    {
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return TestPdf.WriteTemp(Encoding.Latin1.GetBytes(sb.ToString()));
    }

    /// <summary>200×200 page: red square (no layer) bottom-left, blue square in "Blue layer"
    /// (on, locked) bottom-right, green square in "Green layer" (off by default) top-left.</summary>
    private static string LayeredPdf()
    {
        const string content =
            "1 0 0 rg 10 10 80 80 re f " +
            "/OC /L1 BDC 0 0 1 rg 110 10 80 80 re f EMC " +
            "/OC /L2 BDC 0 1 0 rg 10 110 80 80 re f EMC";
        return RawPdf(
            "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [4 0 R 5 0 R] /D << /Name (Default) /ON [4 0 R] /OFF [5 0 R] /Locked [4 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Properties << /L1 4 0 R /L2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /OCG /Name (Blue layer) /Intent /View >>",
            "<< /Type /OCG /Name (Green layer) /Intent [/View /Design] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
    }

    /// <summary>A 720×360 pt page (10 in × 5 in) whose left half is a viewport at
    /// "1 in = 10 ft" (1 pt = 10/72 ft); the right half has no scale.</summary>
    private static string ScaledPdf()
    {
        const string content = "0 0 0 RG 10 10 m 350 10 l S";
        return RawPdf(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 720 360] /Contents 4 0 R " +
            "/VP [<< /Type /Viewport /BBox [0 0 360 360] /Name (Plan) /Measure << /Type /Measure /Subtype /RL /R (1 in = 10 ft) " +
            "/X [<< /Type /NumberFormat /U (ft) /C 0.138889 /D 100 >>] >> >>] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
    }

    private static string AuthoredPdf(int pages = 1, string text = "Discharge summary")
    {
        var b = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            var pb = b.AddPage(new PageSize(595, 842));
            pb.DrawRectangle(0, 0, 595, 842, Color.FromHex("#FFFFFF"), Color.FromHex("#FFFFFF"), 0);
            pb.DrawText($"{text} — page {i + 1}", 60, 400, "Helvetica", 14, Color.FromHex("#000000"));
        }

        return TestPdf.WriteTemp(b.ToByteArray());
    }

    private static (string PfxPath, byte[] CertDer) SelfSignedPfx(string cn, string password, bool ecdsa = false)
    {
        X509Certificate2 cert;
        if (ecdsa)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            cert = new CertificateRequest($"CN={cn}, O=Chuvadi Test", key, HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }
        else
        {
            using var key = RSA.Create(2048);
            cert = new CertificateRequest($"CN={cn}, O=Chuvadi Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        using (cert)
        {
            var path = Path.Combine(Path.GetTempPath(), "chuvadi-test-" + Guid.NewGuid().ToString("N") + ".pfx");
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
            return (path, cert.RawData);
        }
    }

    private static async Task<IPdfSession> Open(string path) =>
        await new ChuvadiPdfReader().OpenAsync(path, Path.GetFileName(path));

    // ── Layers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Layers_are_listed_with_default_state_intents_and_lock()
    {
        using var session = await Open(LayeredPdf());

        Assert.True(session.CanToggleLayers);
        var layers = session.Layers;
        Assert.Equal(2, layers.Count);

        var blue = layers[0];
        Assert.Equal("Blue layer", blue.Name);
        Assert.True(blue.IsVisibleByDefault);
        Assert.True(blue.IsVisible);
        Assert.True(blue.IsLocked);
        Assert.Equal(new[] { "View" }, blue.Intents);

        var green = layers[1];
        Assert.Equal("Green layer", green.Name);
        Assert.False(green.IsVisibleByDefault);
        Assert.False(green.IsVisible);
        Assert.False(green.IsLocked);
        Assert.Contains("Design", green.Intents);
    }

    [Fact]
    public async Task Showing_a_layer_rerenders_the_page_and_leaves_the_file_unchanged()
    {
        var path = LayeredPdf();
        var before = File.ReadAllBytes(path);
        using var session = await Open(path);

        var svgDefault = await session.RenderPageSvgAsync(0);
        Assert.DoesNotContain("rgb(0,255,0)", svgDefault);
        Assert.Contains("rgb(0,0,255)", svgDefault);

        int gen = session.RenderGeneration;
        int events = 0;
        session.RenderInvalidated += (_, _) => events++;

        session.SetLayerVisible("Green layer", true);

        Assert.Equal(gen + 1, session.RenderGeneration);
        Assert.Equal(1, events);
        Assert.False(session.TryGetRenderedSvg(0, out _)); // cache was dropped
        var svgShown = await session.RenderPageSvgAsync(0);
        Assert.Contains("rgb(0,255,0)", svgShown);
        Assert.True(session.Layers.Single(l => l.Name == "Green layer").IsVisible);
        Assert.True(session.Layers.Single(l => l.Name == "Green layer").IsOverridden);

        // Back to the file's default.
        session.ResetLayerVisibility();
        Assert.DoesNotContain("rgb(0,255,0)", await session.RenderPageSvgAsync(0));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Locked_and_unknown_layers_ignore_toggles()
    {
        using var session = await Open(LayeredPdf());
        int gen = session.RenderGeneration;

        session.SetLayerVisible("Blue layer", false); // locked
        session.SetLayerVisible("No such layer", false);
        session.SetLayerVisible("Green layer", false); // already hidden: no-op

        Assert.Equal(gen, session.RenderGeneration);
        Assert.Contains("rgb(0,0,255)", await session.RenderPageSvgAsync(0));
    }

    [Fact]
    public async Task Document_without_layers_reports_none()
    {
        using var session = await Open(TestPdf.TextPage("plain"));
        Assert.Empty(session.Layers);
        Assert.False(session.CanToggleLayers);

        var placeholder = await new PlaceholderPdfReader().OpenAsync("x.pdf", "x.pdf");
        Assert.Empty(placeholder.Layers);
        Assert.False(placeholder.CanToggleLayers);
    }

    // ── Measure ──────────────────────────────────────────────────────────────

    [Fact]
    public void Measure_without_a_file_scale_is_one_to_one_user_space()
    {
        var path = TestPdf.BlankPage(720, 360); // 10 in × 5 in
        var m = new MeasureService().Measure(path, 0, new PagePoint(0.1, 0.5), new PagePoint(0.6, 0.5));

        // 0.5 × 720 pt = 360 pt = 5 in = 127 mm
        Assert.Equal(MeasureScaleSource.PdfUserSpace, m.ScaleSource);
        Assert.Equal("1 : 1", m.ScaleLabel);
        Assert.Equal(360, m.PaperPoints, 6);
        Assert.Equal(5, m.PaperIn, 6);
        Assert.Equal(127, m.PaperMm, 6);
        Assert.Equal(127, m.DistanceMm, 6);
        Assert.Equal(0, m.AngleDegrees, 6);
        Assert.Equal("127.0 mm", m.DistanceLabel);
        Assert.Null(m.FileFormatted);
    }

    [Fact]
    public void Measure_angle_follows_the_screen_and_rotation_keeps_length()
    {
        var path = TestPdf.BlankPage(200, 200);
        var svc = new MeasureService();

        // Up-right diagonal on screen (y decreases downward in fractions) → +45°.
        var diag = svc.Measure(path, 0, new PagePoint(0.25, 0.75), new PagePoint(0.75, 0.25));
        Assert.Equal(45, diag.AngleDegrees, 6);
        Assert.Equal(Math.Sqrt(2) * 100, diag.PaperPoints, 6);

        // Straight down → -90°.
        Assert.Equal(-90, svc.Measure(path, 0, new PagePoint(0.5, 0.1), new PagePoint(0.5, 0.9)).AngleDegrees, 6);

        // A view rotation changes the mapping, not the measured length.
        var rotated = svc.Measure(path, 0, new PagePoint(0.25, 0.75), new PagePoint(0.75, 0.25), viewRotation: 90);
        Assert.Equal(diag.PaperPoints, rotated.PaperPoints, 6);
    }

    [Fact]
    public void Measure_uses_the_viewport_scale_from_the_file()
    {
        var path = ScaledPdf();
        var svc = new MeasureService();

        // Inside the "Plan" viewport (left half): 0.1 → 0.4 of 720 pt = 216 pt = 3 in on paper = 30 ft.
        var m = svc.Measure(path, 0, new PagePoint(0.1, 0.5), new PagePoint(0.4, 0.5));
        Assert.Equal(MeasureScaleSource.FromFile, m.ScaleSource);
        Assert.Equal("1 in = 10 ft (from file)", m.ScaleLabel);
        Assert.Equal("Plan", m.ViewportName);
        Assert.Equal("ft", m.FileUnit);
        Assert.Equal(30, m.FileValue!.Value, 2);
        Assert.Equal(30 * 304.8, m.DistanceMm, 0);
        Assert.Equal(3 * 25.4, m.PaperMm, 6);
        Assert.False(string.IsNullOrWhiteSpace(m.FileFormatted));

        // Spanning outside the viewport → 1:1.
        var outside = svc.Measure(path, 0, new PagePoint(0.6, 0.5), new PagePoint(0.9, 0.5));
        Assert.Equal(MeasureScaleSource.PdfUserSpace, outside.ScaleSource);
        var straddle = svc.Measure(path, 0, new PagePoint(0.1, 0.5), new PagePoint(0.9, 0.5));
        Assert.Equal(MeasureScaleSource.PdfUserSpace, straddle.ScaleSource);

        // Polyline inside the viewport: 216 pt + 72 pt = 4 in on paper = 40 ft.
        var poly = svc.MeasurePolyline(path, 0,
            new[] { new PagePoint(0.1, 0.5), new PagePoint(0.4, 0.5), new PagePoint(0.4, 0.3) }, closed: false);
        Assert.Equal(MeasureScaleSource.FromFile, poly.ScaleSource);
        Assert.Equal("40 ft", poly.FileLengthFormatted);
        Assert.Null(poly.AreaLabel);
    }

    [Fact]
    public async Task Measure_works_on_an_open_session_and_polyline_area()
    {
        var path = TestPdf.BlankPage(720, 720); // 10 in square
        using var session = await Open(path);
        var svc = new MeasureService();

        var m = svc.Measure(session, 0, new PagePoint(0, 0), new PagePoint(1, 0));
        Assert.NotNull(m);
        Assert.Equal(254, m!.PaperMm, 6);

        // Closed square 0.1..0.6 → 5 in × 5 in = 25 in², perimeter 20 in.
        var poly = svc.MeasurePolyline(session, 0,
            new[] { new PagePoint(0.1, 0.1), new PagePoint(0.6, 0.1), new PagePoint(0.6, 0.6), new PagePoint(0.1, 0.6) },
            closed: true);
        Assert.NotNull(poly);
        Assert.True(poly!.Closed);
        Assert.Equal(20, poly.PaperLengthIn, 6);
        Assert.Equal(25, poly.PaperAreaIn2!.Value, 6);
        Assert.Equal(25 * 25.4 * 25.4, poly.PaperAreaMm2!.Value, 3);

        var placeholder = await new PlaceholderPdfReader().OpenAsync("x.pdf", "x.pdf");
        Assert.Null(svc.Measure(placeholder, 0, new PagePoint(0, 0), new PagePoint(1, 1)));
    }

    // ── Signatures ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Unsigned_file_has_no_signatures()
    {
        var report = await new SignatureCheckService().CheckAsync(AuthoredPdf());
        Assert.Null(report.Error);
        Assert.False(report.HasSignatures);
        Assert.Equal("Not signed", report.Headline);
    }

    [Fact]
    public async Task Signed_copy_verifies_and_trust_depends_on_the_trust_list()
    {
        var src = AuthoredPdf();
        var srcBytes = File.ReadAllBytes(src);
        var (pfx, der) = SelfSignedPfx("A. Shiva", "pfx-pass");
        var output = TestPdf.TempOutput();

        await TestSigning.SignWithPfxAsync(src, output, pfx, "pfx-pass",
            new CertificateSignOptions { Reason = "Approved for discharge", Location = "Pune" });

        Assert.Equal(srcBytes, File.ReadAllBytes(src)); // source untouched
        var svc = new SignatureCheckService();

        // Self-signed: intact, but not issued by a trusted root.
        var untrusted = await svc.CheckAsync(output, options: new SignatureCheckOptions { UseSystemTrustStore = false });
        var s = Assert.Single(untrusted.Signatures);
        Assert.Equal("A. Shiva", s.SignerName);
        Assert.Equal("Approved for discharge", s.Reason);
        Assert.Equal("Pune", s.Location);
        Assert.Equal("adbe.pkcs7.detached", s.SubFilter);
        Assert.True(s.CoversWholeDocument);
        Assert.True(s.IntegrityVerified);
        Assert.False(s.TrustValidated);
        Assert.Equal(SignatureCheckStatus.UnknownTrust, s.Status);
        Assert.NotNull(s.SigningTime);
        Assert.StartsWith("Signed by A. Shiva · ", s.Summary);
        Assert.Contains("document unchanged since signing", s.Summary);

        // With the certificate trusted explicitly → valid.
        var trusted = await svc.CheckAsync(output, options: new SignatureCheckOptions { UseSystemTrustStore = false, TrustedCertificates = new[] { der } });
        var t = Assert.Single(trusted.Signatures);
        Assert.Equal(SignatureCheckStatus.Valid, t.Status);
        Assert.True(t.TrustValidated);
        var day = t.SigningTime!.Value.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Signed by A. Shiva · {day} · certificate valid · document unchanged since signing", t.Summary);
        Assert.True(trusted.AllValid);
        Assert.Equal("1 signature valid", trusted.Headline);
    }

    [Fact]
    public async Task Ecdsa_certificate_signs_too()
    {
        var (pfx, der) = SelfSignedPfx("EC Signer", "pw", ecdsa: true);
        var output = TestPdf.TempOutput();
        await TestSigning.SignWithPfxAsync(AuthoredPdf(), output, pfx, "pw");

        var report = await new SignatureCheckService().CheckAsync(output,
            options: new SignatureCheckOptions { UseSystemTrustStore = false, TrustedCertificates = new[] { der } });
        Assert.Equal(SignatureCheckStatus.Valid, Assert.Single(report.Signatures).Status);
    }

    [Fact]
    public async Task Changes_after_signing_are_reported()
    {
        var (pfx, der) = SelfSignedPfx("A. Shiva", "pw");
        var signedPath = TestPdf.TempOutput();
        await TestSigning.SignWithPfxAsync(AuthoredPdf(), signedPath, pfx, "pw");
        var signed = File.ReadAllBytes(signedPath);
        var opts = new SignatureCheckOptions { UseSystemTrustStore = false, TrustedCertificates = new[] { der } };
        var svc = new SignatureCheckService();

        // Bytes appended after the signed range (an incremental update would do this).
        var appended = TestPdf.WriteTemp(signed.Concat(Encoding.ASCII.GetBytes("\n% later revision\n")).ToArray());
        var a = Assert.Single((await svc.CheckAsync(appended, options: opts)).Signatures);
        Assert.False(a.CoversWholeDocument);
        Assert.Equal(SignatureCheckStatus.ModifiedAfterSigning, a.Status);
        Assert.Contains("document changed after signing", a.Summary);

        // A byte inside the signed range flipped → broken.
        var tampered = (byte[])signed.Clone();
        int idx = Encoding.Latin1.GetString(tampered).IndexOf("/Producer", StringComparison.Ordinal);
        Assert.True(idx > 0);
        tampered[idx + 2] ^= 0x01;
        var b = Assert.Single((await svc.CheckAsync(TestPdf.WriteTemp(tampered), options: opts)).Signatures);
        Assert.Equal(SignatureCheckStatus.Invalid, b.Status);
        Assert.False(b.IntegrityVerified);
    }

    [Fact]
    public async Task Second_signature_keeps_the_first_intact()
    {
        var (pfx, der) = SelfSignedPfx("A. Shiva", "pw");
        var once = TestPdf.TempOutput();
        var twice = TestPdf.TempOutput();
        await TestSigning.SignWithPfxAsync(AuthoredPdf(), once, pfx, "pw");
        await TestSigning.SignWithPfxAsync(once, twice, pfx, "pw", new CertificateSignOptions { Reason = "Countersigned" });

        var report = await new SignatureCheckService().CheckAsync(twice,
            options: new SignatureCheckOptions { UseSystemTrustStore = false, TrustedCertificates = new[] { der } });
        Assert.Equal(2, report.Signatures.Count);
        Assert.Equal("Signature1", report.Signatures[0].FieldName);
        Assert.Equal("Signature2", report.Signatures[1].FieldName);
        Assert.Equal(SignatureCheckStatus.ModifiedAfterSigning, report.Signatures[0].Status);
        Assert.True(report.Signatures[0].IntegrityVerified);
        Assert.Equal(SignatureCheckStatus.Valid, report.Signatures[1].Status);
    }

    [Fact]
    public async Task App_signing_waits_on_the_library_but_reads_the_certificate()
    {
        var src = AuthoredPdf();
        var (pfx, _) = SelfSignedPfx("A. Shiva", "pw");
        var svc = new CertificateSignService();

        Assert.False(CertificateSignService.IsAvailable);
        await Assert.ThrowsAsync<NotSupportedException>(() => svc.SignWithPfxAsync(src, TestPdf.TempOutput(), pfx, "pw"));
        Assert.ThrowsAny<CryptographicException>(() => svc.InspectPfx(pfx, "wrong"));

        var info = svc.InspectPfx(pfx, "pw");
        Assert.Equal("A. Shiva", info.SubjectName);
        Assert.True(info.HasPrivateKey);
        Assert.True(info.IsCurrentlyValid);
        Assert.StartsWith("RSA", info.KeyAlgorithm);
        Assert.False(svc.SupportsVisibleAppearance);
    }

    [Fact]
    public async Task Unreadable_file_reports_an_error_not_an_exception()
    {
        var bogus = TestPdf.WriteTemp(Encoding.ASCII.GetBytes("not a pdf"));
        var report = await new SignatureCheckService().CheckAsync(bogus);
        Assert.NotNull(report.Error);
        Assert.False(report.HasSignatures);
    }

    // ── Print page ranges ────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 5, 0, new[] { 0, 1, 2, 3, 4 })]
    [InlineData("all", 3, 0, new[] { 0, 1, 2 })]
    [InlineData("current", 5, 3, new[] { 3 })]
    [InlineData("1-3, 5", 5, 0, new[] { 0, 1, 2, 4 })]
    [InlineData("2; 4–5", 5, 0, new[] { 1, 3, 4 })]
    [InlineData("3-", 5, 0, new[] { 2, 3, 4 })]
    [InlineData("-2", 5, 0, new[] { 0, 1 })]
    [InlineData("2, 2, 1-2", 5, 0, new[] { 1, 0 })]
    public void Page_ranges_parse(string text, int count, int current, int[] expected)
    {
        Assert.True(PageRangeParser.TryParse(text, count, current, out var pages, out var error), error);
        Assert.Equal(expected, pages);
    }

    [Theory]
    [InlineData("0", 5)]
    [InlineData("6", 5)]
    [InlineData("4-2", 5)]
    [InlineData("a-b", 5)]
    [InlineData("1.5", 5)]
    [InlineData(",", 5)]
    public void Bad_page_ranges_are_rejected_with_a_message(string text, int count)
    {
        Assert.False(PageRangeParser.TryParse(text, count, 0, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Noop_print_service_is_unavailable()
    {
        var svc = new NoopPrintService();
        Assert.False(svc.IsAvailable);
        var r = await svc.PrintAsync("x.pdf", new PrintRequest());
        Assert.Equal(PrintOutcome.Unavailable, r.Outcome);
        Assert.Equal("Printing isn't available on this platform", r.Message);
    }
}
