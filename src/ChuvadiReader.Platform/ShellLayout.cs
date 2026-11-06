namespace ChuvadiReader.Platform;

/// <summary>
/// The one value the Ui switches its chrome on. Derived from <see cref="PlatformKind"/> and
/// <see cref="FormFactor"/>; hosts never set it directly.
/// </summary>
public enum ShellLayout
{
    /// <summary>Windows, macOS, Linux: D-10 side rails, window buttons (Reader Side Rails).</summary>
    Desktop,

    /// <summary>Android tablet: same rails at 72 / 72 / 60 px, 48 px targets (Portal &amp; Android 12d-12f).</summary>
    Tablet,

    /// <summary>Android phone: bottom bar, tool strip, View sheet (Portal &amp; Android 12g-12j).</summary>
    Phone,

    /// <summary>Web portal in a desktop-sized browser: desktop rails, account menu, Sangam sign-in,
    /// server status text (Portal &amp; Android 12a-12c). In a phone- or tablet-sized browser the layout is
    /// <see cref="Phone"/> / <see cref="Tablet"/> and the portal chrome still applies
    /// (<see cref="ShellLayouts.IsPortal(IPlatformInfo)"/>, <see cref="ShellChrome.AccountMenu"/>).</summary>
    Portal,
}

/// <summary>Derivation rule for <see cref="ShellLayout"/>, shared by every host and the Ui.</summary>
public static class ShellLayouts
{
    /// <summary>
    /// The form factor decides the layout on every platform (decision F-5: Web follows the browser).
    /// A desktop-sized Web view is <see cref="ShellLayout.Portal"/>; Web in a phone or tablet browser
    /// uses the <see cref="ShellLayout.Phone"/> / <see cref="ShellLayout.Tablet"/> layouts.
    /// </summary>
    public static ShellLayout From(PlatformKind kind, FormFactor formFactor) => formFactor switch
    {
        FormFactor.Tablet => ShellLayout.Tablet,
        FormFactor.Phone => ShellLayout.Phone,
        _ => kind == PlatformKind.Web ? ShellLayout.Portal : ShellLayout.Desktop,
    };

    /// <summary>True on the Web portal at any browser size: show portal chrome (account menu, sign-in,
    /// server status text) whatever the layout.</summary>
    public static bool IsPortal(this IPlatformInfo info) => info.Kind == PlatformKind.Web;

    /// <summary>The value written to <c>&lt;html data-shell="…"&gt;</c>.</summary>
    public static string Key(ShellLayout layout) => layout switch
    {
        ShellLayout.Tablet => "tablet",
        ShellLayout.Phone => "phone",
        ShellLayout.Portal => "portal",
        _ => "desktop",
    };

    /// <summary>Value for <c>&lt;html data-platform="…"&gt;</c>.</summary>
    public static string PlatformKey(PlatformKind kind) => kind switch
    {
        PlatformKind.Windows => "windows",
        PlatformKind.MacOS => "macos",
        PlatformKind.Linux => "linux",
        PlatformKind.Android => "android",
        _ => "web",
    };

    /// <summary>Value for <c>&lt;html data-form-factor="…"&gt;</c>.</summary>
    public static string FormFactorKey(FormFactor formFactor) => formFactor switch
    {
        FormFactor.Tablet => "tablet",
        FormFactor.Phone => "phone",
        _ => "desktop",
    };

    /// <summary>Value for <c>&lt;html data-input="…"&gt;</c>, e.g. "touch pen".</summary>
    public static string InputKey(InputKinds input) => string.Join(' ', new[]
    {
        input.HasFlag(InputKinds.Mouse) ? "mouse" : null,
        input.HasFlag(InputKinds.Touch) ? "touch" : null,
        input.HasFlag(InputKinds.Pen) ? "pen" : null,
        input.HasFlag(InputKinds.Keyboard) ? "keyboard" : null,
    }.Where(s => s is not null));

    /// <summary>Shorthand for <c>From(info.Kind, info.FormFactor)</c>.</summary>
    public static ShellLayout Shell(this IPlatformInfo info) => From(info.Kind, info.FormFactor);
}
