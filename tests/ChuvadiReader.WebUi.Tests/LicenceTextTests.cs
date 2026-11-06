using ChuvadiReader.Ui.Components.Legal;
using Xunit;

namespace ChuvadiReader.WebUi.Tests;

/// <summary>The legal pages show exactly the files that ship, all still marked as drafts.</summary>
public class LicenceTextTests
{
    private static string Repo(params string[] parts)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "LICENSE")))
        {
            d = d.Parent;
        }

        return Path.Combine([d!.FullName, .. parts]);
    }

    public static TheoryData<string, string> Docs => new()
    {
        { "eula", "legal/EULA.txt" },
        { "licence", "LICENSE" },
        { "terms", "legal/TERMS-OF-USE.txt" },
        { "privacy", "legal/PRIVACY-NOTICE.txt" },
        { "cookies", "legal/COOKIE-NOTICE.txt" },
        { "notices", "legal/THIRD-PARTY-NOTICES.txt" },
    };

    private static LegalDoc Doc(string id) =>
        LegalTexts.AppDocs.Concat(LegalTexts.PortalDocs).First(d => d.Id == id);

    [Theory]
    [MemberData(nameof(Docs))]
    public void Shown_text_matches_the_shipped_file(string id, string file) =>
        Assert.Equal(File.ReadAllText(Repo(file.Split('/'))), LegalTexts.Text(Doc(id)));

    [Theory]
    [MemberData(nameof(Docs))]
    public void Every_document_is_marked_for_counsel_and_carries_the_copyright(string id, string file)
    {
        var text = LegalTexts.Text(Doc(id));
        Assert.StartsWith("DRAFT, to be reviewed by counsel.", text);
        Assert.Contains("Copyright © 2026 Sthraas Intelligence Pvt Ltd. All rights reserved.", text);
        _ = file;
    }

    [Fact]
    public void Apps_show_the_EULA_and_the_portal_shows_the_terms()
    {
        Assert.Equal(["eula", "licence", "privacy", "notices"], LegalTexts.AppDocs.Select(d => d.Id));
        Assert.Equal(["terms", "privacy", "cookies", "notices"], LegalTexts.PortalDocs.Select(d => d.Id));
    }

    [Fact]
    public void Disputes_go_to_Ahmedabad()
    {
        foreach (var id in new[] { "eula", "licence", "terms", "privacy" })
        {
            Assert.Contains("Ahmedabad, Gujarat, India", LegalTexts.Text(Doc(id)));
        }
    }
}
