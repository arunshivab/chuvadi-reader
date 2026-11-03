using ChuvadiReader.Core.Reader;
using Xunit;

namespace ChuvadiReader.Tests;

public class ShellServiceTests
{
    private static ShellCommand Cmd(string id, string label, string group = "G", string? why = null, string? keywords = null)
        => new(id, label, group, "x", null, () => Task.CompletedTask, why, keywords);

    [Fact]
    public void Modes_are_remembered_per_tab()
    {
        var s = new ShellService();
        s.SetMode(DocMode.Mark, "a");
        s.SetMode(DocMode.Protect, "b");
        Assert.Equal(DocMode.Mark, s.ModeFor("a"));
        Assert.Equal(DocMode.Protect, s.ModeFor("b"));
        Assert.Equal(DocMode.Read, s.ModeFor("unknown"));
        Assert.Equal(DocMode.Protect, s.Mode);
        s.ForgetTab("a");
        Assert.Equal(DocMode.Read, s.ModeFor("a"));
    }

    [Fact]
    public void Gerunds_match_the_design()
    {
        Assert.Equal("Reading", ShellService.Gerund(DocMode.Read));
        Assert.Equal("Marking", ShellService.Gerund(DocMode.Mark));
        Assert.Equal("Arranging", ShellService.Gerund(DocMode.Arrange));
        Assert.Equal("Protecting", ShellService.Gerund(DocMode.Protect));
    }

    [Fact]
    public async Task Counter_clamps_and_calls_the_owner()
    {
        var s = new ShellService();
        var got = 0;
        s.CounterHandler = p => { got = p; return Task.CompletedTask; };
        s.SetCounter(2, 13);
        await s.GoToAsync(99);
        Assert.Equal(13, got);
        await s.GoToAsync(-4);
        Assert.Equal(1, got);
        s.ClearCounter();
        Assert.False(s.HasCounter);
    }

    [Fact]
    public void Search_ranks_prefix_then_word_then_substring_then_keywords()
    {
        var list = new[]
        {
            Cmd("1", "Export pages as images"),
            Cmd("2", "Zoom in"),
            Cmd("3", "Reduce file size (Press)", keywords: "compress"),
            Cmd("4", "Print"),
            Cmd("5", "Fit page"),
        };
        var r = ShellService.Search(list, "p");
        Assert.Equal("4", r[0].Id);          // label prefix
        Assert.Contains(r, c => c.Id == "1"); // word prefix ("pages")
        Assert.Equal("3", ShellService.Search(list, "compress").Single().Id);
        Assert.Empty(ShellService.Search(list, "zzz"));
        Assert.Equal(list.Length, ShellService.Search(list, "  ").Count);
    }

    [Fact]
    public void Disabled_commands_stay_listed_after_enabled_ones()
    {
        var list = new[] { Cmd("a", "Print later", why: "Coming soon"), Cmd("b", "Print now") };
        var r = ShellService.Search(list, "print");
        Assert.Equal("b", r[0].Id);
        Assert.False(r[1].IsEnabled);
    }

    [Fact]
    public void Registered_commands_are_deduplicated_and_removable()
    {
        var s = new ShellService();
        var owner1 = new object();
        var owner2 = new object();
        s.RegisterCommands(owner1, new[] { Cmd("x", "One") });
        s.RegisterCommands(owner2, new[] { Cmd("x", "Two"), Cmd("y", "Three") });
        Assert.Equal(2, s.Commands.Count);
        s.UnregisterCommands(owner2);
        Assert.Equal("One", s.Commands.Single().Label);
    }

    [Fact]
    public void Messages_keep_the_latest_four_and_can_be_dismissed()
    {
        var s = new ShellService();
        ShellMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            last = s.Notify("m" + i);
        }

        Assert.Equal(4, s.Messages.Count);
        s.Dismiss(last!.Id);
        Assert.Equal(3, s.Messages.Count);
    }
}
