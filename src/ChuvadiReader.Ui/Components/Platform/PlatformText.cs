using ChuvadiReader.Platform;

namespace ChuvadiReader.Ui.Components.Platform;

/// <summary>Wording that differs by platform: the Web portal (D-20, U-11) and Android (6 Nov 2026).
/// Desktop strings are the original text, character for character.</summary>
public static class PlatformText
{
    /// <summary>Replaces the device-only privacy lines (About, Settings, Library) on the Web portal.
    /// The status bar keeps the handout's own 12b text (see StatusLocation).</summary>
    public const string ServerPrivacy =
        "Processed on the Chuvadi server · encrypted · only you can see it. Your files are removed when you sign out.";

    /// <summary>End of the Home summary line on the Web portal (U-11); desktop says "nothing leaves this device.".</summary>
    public const string HomeServerLine = "processed on the Chuvadi server · removed when you sign out.";

    /// <summary>"phone" on an Android phone, "device" on an Android tablet, "computer" everywhere else.</summary>
    public static string DeviceNoun(IPlatformInfo platform) =>
        platform.Kind != PlatformKind.Android ? "computer"
        : platform.FormFactor == FormFactor.Phone ? "phone" : "device";

    /// <summary>Appearance › the follow-the-system toggle. The portal can only follow the browser (W-2).</summary>
    public static string FollowSystemTheme(IPlatformInfo platform) =>
        platform.Kind == PlatformKind.Web
            ? "Follow the browser's light / dark setting"
            : $"Follow the {DeviceNoun(platform)}'s light / dark setting";

    /// <summary>Language › Greeting › Preferred name hint. Android shows no name until one is set
    /// (D-59), so its hint says so instead of promising the device's name.</summary>
    public static string PreferredNameHint(IPlatformInfo platform) => platform.Kind switch
    {
        PlatformKind.Android => $"Leave blank to show no name. Stored on this {DeviceNoun(platform)}, never sent anywhere.",
        PlatformKind.Web => "Leave blank to use your Sangam name. Kept until you sign out.", // W-3
        _ => "Leave blank to use your computer's name. Stored locally, never sent anywhere.",
    };

    /// <summary>Library with no PDFs on the portal, which has no category folders (W-1).</summary>
    public const string LibraryEmptyServer = "No PDFs here yet. Use Upload to open documents. Files are removed when you sign out.";

    /// <summary>Home's first-run tip on the phone layout, which has no Ctrl key (W-4).</summary>
    public const string PhoneFindTip = "Tip: tap “Find or do anything” to reach any file or action. Try typing “merge”.";
}
