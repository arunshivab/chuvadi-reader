namespace ChuvadiReader.Ui.Components.Platform.Portal;

/// <summary>
/// Addresses the web portal's chrome links to. Registered by the Web host only (desktop builds never
/// render portal components). Values come from the Web host's configuration and its Sangam client.
/// </summary>
/// <param name="DesktopAppUrl">"Download the desktop app" (12a) and "Get the desktop app" (12b account
/// menu). Configuration; placeholder https://chuvadi.in/download until the domain is final (D-35).</param>
/// <param name="AccountSettingsUrl">"Account settings" in the account menu (12b): Sangam's own account
/// page (D-35). Null hides the item.</param>
/// <param name="TermsUrl">"Terms of use" under the sign-in card (12a foot): the EULA the Foundation adds
/// (licence: proprietary, free for everyone). Optional; null hides the line.</param>
public sealed record PortalLinks(string DesktopAppUrl, string? AccountSettingsUrl, string? TermsUrl = null)
{
    /// <summary>D-35 placeholder for <see cref="DesktopAppUrl"/>.</summary>
    public const string PlaceholderDesktopAppUrl = "https://chuvadi.in/download";
}
