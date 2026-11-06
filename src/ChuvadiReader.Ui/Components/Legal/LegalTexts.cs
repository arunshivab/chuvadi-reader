using System.Collections.Concurrent;

namespace ChuvadiReader.Ui.Components.Legal;

/// <summary>One legal document the app shows.</summary>
/// <param name="Id">Stable id, used in addresses (/licence?part=notices).</param>
/// <param name="Label">Tab label.</param>
/// <param name="Resource">Embedded resource name (from LICENSE or legal/*.txt).</param>
public sealed record LegalDoc(string Id, string Label, string Resource);

/// <summary>
/// The legal texts shipped with every host, embedded from the repository's <c>LICENSE</c> and
/// <c>legal/*.txt</c> (one source; a text is read only when its page opens, never at startup).
/// </summary>
public static class LegalTexts
{
    public static readonly LegalDoc Eula = new("eula", "Licence agreement", "Chuvadi.Legal.EULA.txt");
    public static readonly LegalDoc Licence = new("licence", "Licence", "Chuvadi.Legal.LICENSE.txt");
    public static readonly LegalDoc Terms = new("terms", "Terms of use", "Chuvadi.Legal.TERMS-OF-USE.txt");
    public static readonly LegalDoc Privacy = new("privacy", "Privacy", "Chuvadi.Legal.PRIVACY-NOTICE.txt");
    public static readonly LegalDoc Cookies = new("cookies", "Cookies", "Chuvadi.Legal.COOKIE-NOTICE.txt");
    public static readonly LegalDoc Notices = new("notices", "Third-party notices", "Chuvadi.Legal.THIRD-PARTY-NOTICES.txt");

    /// <summary>What the apps show (desktop and mobile).</summary>
    public static readonly IReadOnlyList<LegalDoc> AppDocs = [Eula, Licence, Privacy, Notices];

    /// <summary>What the web portal shows.</summary>
    public static readonly IReadOnlyList<LegalDoc> PortalDocs = [Terms, Privacy, Cookies, Notices];

    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>The document's text, exactly as shipped.</summary>
    public static string Text(LegalDoc doc) => Cache.GetOrAdd(doc.Resource, Read);

    private static string Read(string name)
    {
        using var stream = typeof(LegalTexts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
