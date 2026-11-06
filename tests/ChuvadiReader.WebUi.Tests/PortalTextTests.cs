using ChuvadiReader.Ui.Components.Platform.Portal;
using Xunit;

namespace ChuvadiReader.WebUi.Tests;

public class AccountTextTests
{
    [Theory]
    [InlineData("Arun S.", "AS")]
    [InlineData("arun shiva balasubramanian", "AB")]
    [InlineData("Arun", "A")]
    [InlineData("  Arun   S.  ", "AS")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    [InlineData("(Records) Arun", "A")]
    public void Initials(string? name, string expected) => Assert.Equal(expected, AccountText.Initials(name));

    [Theory]
    [InlineData("Arun S.", "Arun")]
    [InlineData("Arun", "Arun")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void FirstName(string? name, string expected) => Assert.Equal(expected, AccountText.FirstName(name));
}

public class SignInTextTests
{
    [Fact]
    public void Promises_follow_D31()
    {
        Assert.Equal(
            ["Files live only for your session, then are wiped",
             "Only your Sangam identity can open your files",
             "Sign out and your files are gone from the server"],
            PortalSignIn.PromiseTexts);
    }

    [Fact]
    public void No_promise_claims_per_user_storage_or_delete()
    {
        Assert.DoesNotContain(PortalSignIn.PromiseTexts, t => t.Contains("per-user storage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PortalSignIn.PromiseTexts, t => t.StartsWith("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sangam_id_field_is_off_by_default_U9a() => Assert.False(PortalSignIn.ShowSangamIdDefault);

    [Fact]
    public void Terms_link_is_optional_and_two_argument_construction_still_works()
    {
        var links = new PortalLinks(PortalLinks.PlaceholderDesktopAppUrl, null);
        Assert.Null(links.TermsUrl);
        Assert.Equal("/terms", (links with { TermsUrl = "/terms" }).TermsUrl);
    }

    [Fact]
    public void Desktop_app_placeholder_is_D35()
        => Assert.Equal("https://chuvadi.in/download", PortalLinks.PlaceholderDesktopAppUrl);
}

public class GatingRowsTests
{
    [Fact]
    public void Release1_list_has_no_sharing_row()
    {
        Assert.DoesNotContain(SangamGatingRow.Release1, r => r.Text.Contains("Share", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(7, SangamGatingRow.Release1.Count);
    }
}
