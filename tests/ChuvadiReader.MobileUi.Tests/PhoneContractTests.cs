using System.Text.RegularExpressions;
using ChuvadiReader.Ui.Components.Platform.Phone;
using Xunit;

namespace ChuvadiReader.MobileUi.Tests;

/// <summary>
/// The phone chrome runs the commands the mode views register with ShellService, by id. If a mode
/// view renames or drops one, the strip / sheet / FAB would silently lose it — these tests fail
/// instead. They read the registrations as text, like the Foundation's drift test.
/// </summary>
public class PhoneContractTests
{
    private static string Sources()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "ShellSources");
        return string.Join('\n', Directory.GetFiles(dir, "*.Shell.cs").Select(File.ReadAllText));
    }

    [Fact]
    public void Every_command_id_the_phone_uses_is_registered_by_a_mode_view()
    {
        var src = Sources();
        var missing = PhoneCommands.AllIds
            .Where(id => !src.Contains($"\"{id}\"", StringComparison.Ordinal)
                         && !(id.StartsWith("m.", StringComparison.Ordinal) && MarkLoopRegisters(src, id)))
            .ToList();
        Assert.Empty(missing);
    }

    // Reader.Shell.cs registers the drawing tools in a loop: new("m." + t, …) over ("text", …), ("rect", …) …
    private static bool MarkLoopRegisters(string src, string id) =>
        src.Contains("\"m.\" + t", StringComparison.Ordinal)
        && src.Contains($"(\"{id[2..]}\",", StringComparison.Ordinal);

    [Fact]
    public void Strip_and_sheet_match_the_design_frames()
    {
        Assert.Equal(new[] { "Text", "Mark", "Note", "Image", "Box", "Draw", "Sign" }, PhoneCommands.Mark.Select(i => i.Label));
        Assert.Equal(new[] { "Box", "Find", "Stamp", "Flatten", "Lock", "Sign" }, PhoneCommands.Protect.Select(i => i.Label));
        Assert.Equal(new[] { "Fit width", "Fit page", "Two-page", "Rotate view", "Loupe" }, PhoneCommands.View.Select(r => r.Label));
    }

    [Fact]
    public void Every_mobile_rule_is_scoped_to_the_tablet_or_phone_layout()
    {
        // Desktop and portal must be untouched: every selector in mobile.css is keyed on
        // html[data-shell="tablet|phone"], targets the phone shell's own .ph-* classes, or is
        // the shared immersive/keyframe/reduced-motion boilerplate for those classes.
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mobile.css.txt"));
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        css = Regex.Replace(css, @"@keyframes[^{]+\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}", "");
        css = Regex.Replace(css, @"@media[^{]+\{", "");
        var bad = new List<string>();
        foreach (Match m in Regex.Matches(css, @"([^{}]+)\{[^{}]*\}"))
        {
            foreach (var raw in m.Groups[1].Value.Split(','))
            {
                var sel = raw.Trim();
                if (sel.Length == 0) continue;
                var ok = sel.StartsWith("html[data-shell=\"tablet\"]", StringComparison.Ordinal)
                         || sel.StartsWith("html[data-shell=\"phone\"]", StringComparison.Ordinal)
                         || sel.StartsWith(".ph-", StringComparison.Ordinal)
                         || (sel.StartsWith("body.chvd-immersive .ph-", StringComparison.Ordinal));
                if (!ok) bad.Add(sel);
            }
        }

        Assert.Empty(bad);
    }
}
