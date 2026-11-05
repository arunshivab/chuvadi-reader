using ChuvadiReader.Core.Window;
using Xunit;

namespace ChuvadiReader.Tests;

public class HostServicesTests
{
    [Theory]
    [InlineData("https://example.com/a?b=c", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("data:text/html,hi", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("#section", false)]
    [InlineData("relative/page.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_web_and_mail_links_are_handed_to_the_system(string? url, bool allowed)
        => Assert.Equal(allowed, IExternalLinks.IsAllowed(url));

    [Fact]
    public async Task Hosts_without_support_say_so()
    {
        Assert.False(new NoopUiScale().IsSupported);
        new NoopUiScale().SetScale(150); // no-op, no throw
        var links = new NoExternalLinks();
        Assert.False(links.IsAvailable);
        Assert.False(await links.OpenAsync("https://example.com"));
    }
}
