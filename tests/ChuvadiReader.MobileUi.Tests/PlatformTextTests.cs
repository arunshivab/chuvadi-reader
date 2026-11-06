using ChuvadiReader.Platform;
using ChuvadiReader.Ui.Components.Platform;
using Xunit;

namespace ChuvadiReader.MobileUi.Tests;

/// <summary>Per-platform wording: desktop keeps the original text exactly; Android says phone/device.</summary>
public class PlatformTextTests
{
    private sealed class Info(PlatformKind kind, FormFactor formFactor) : IPlatformInfo
    {
        public PlatformKind Kind => kind;
        public FormFactor FormFactor => formFactor;
        public event Action? FormFactorChanged { add { } remove { } }
        public InputKinds Input => default;
        public ShellChrome Chrome => null!;
        public PlatformCapabilities Capabilities => null!;
        public IKeyLabels Keys => null!;
    }

    [Theory]
    [InlineData(PlatformKind.Windows, FormFactor.Desktop, "Follow the computer's light / dark setting")]
    [InlineData(PlatformKind.MacOS, FormFactor.Desktop, "Follow the computer's light / dark setting")]
    [InlineData(PlatformKind.Linux, FormFactor.Desktop, "Follow the computer's light / dark setting")]
    [InlineData(PlatformKind.Android, FormFactor.Phone, "Follow the phone's light / dark setting")]
    [InlineData(PlatformKind.Android, FormFactor.Tablet, "Follow the device's light / dark setting")]
    [InlineData(PlatformKind.Web, FormFactor.Phone, "Follow the browser's light / dark setting")]
    [InlineData(PlatformKind.Web, FormFactor.Desktop, "Follow the browser's light / dark setting")]
    public void Follow_system_theme(PlatformKind kind, FormFactor ff, string expected) =>
        Assert.Equal(expected, PlatformText.FollowSystemTheme(new Info(kind, ff)));

    [Theory]
    [InlineData(PlatformKind.Windows, FormFactor.Desktop, "Leave blank to use your computer's name. Stored locally, never sent anywhere.")]
    [InlineData(PlatformKind.Android, FormFactor.Phone, "Leave blank to show no name. Stored on this phone, never sent anywhere.")]
    [InlineData(PlatformKind.Android, FormFactor.Tablet, "Leave blank to show no name. Stored on this device, never sent anywhere.")]
    [InlineData(PlatformKind.Web, FormFactor.Desktop, "Leave blank to use your Sangam name. Kept until you sign out.")]
    [InlineData(PlatformKind.MacOS, FormFactor.Desktop, "Leave blank to use your computer's name. Stored locally, never sent anywhere.")]
    public void Preferred_name_hint(PlatformKind kind, FormFactor ff, string expected) =>
        Assert.Equal(expected, PlatformText.PreferredNameHint(new Info(kind, ff)));
}
