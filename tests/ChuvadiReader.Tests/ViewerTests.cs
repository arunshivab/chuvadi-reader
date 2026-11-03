using System.Text;
using ChuvadiReader.Core.Documents;
using Xunit;
using Path = System.IO.Path;

namespace ChuvadiReader.Tests;

/// <summary>Guards for the D-09 view-only viewers' Core: Markdown rendering, HTML sanitising and
/// text loading.</summary>
public class ViewerTests
{
    // ── MarkdownRenderer ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Markdown_headings_get_slugs_sections_and_outline()
    {
        var r = MarkdownRenderer.Render("# Ward 4 — night handover\n\nIntro.\n\n## Before 22:00\n\nText\n\nSetext Two\n----------\n\n## Before 22:00\n\n#### Deep");

        Assert.Contains("<h1 data-anchor=\"ward-4--night-handover\" data-sec=\"1\">Ward 4 — night handover</h1>", r.Html);
        Assert.Contains("<h2 data-anchor=\"before-2200\" data-sec=\"2\">", r.Html);
        Assert.Contains("<h2 data-anchor=\"setext-two\" data-sec=\"3\">Setext Two</h2>", r.Html);
        Assert.Contains("data-anchor=\"before-2200-1\"", r.Html); // de-duplicated
        Assert.Contains("<h4 data-anchor=\"deep\">Deep</h4>", r.Html); // not in the outline
        Assert.Equal(4, r.Outline.Count);
        Assert.Equal(new ViewerOutlineEntry("Before 22:00", 2, "before-2200"), r.Outline[1]);
        Assert.Equal(1, r.Outline[0].Level);
        Assert.DoesNotContain(" id=", r.Html); // never ids: they would collide with the app's DOM
    }

    [Fact]
    public void Markdown_atx_closing_hashes_and_setext_h1()
    {
        var r = MarkdownRenderer.Render("## Title ##\n\nMain\n===");
        Assert.Contains(">Title</h2>", r.Html);
        Assert.Contains("<h1 data-anchor=\"main\" data-sec=\"2\">Main</h1>", r.Html);
    }

    [Fact]
    public void Markdown_inline_emphasis_strong_code_and_strike()
    {
        var r = MarkdownRenderer.Render("This is *em*, **strong**, ***both***, _under_, `a < b`, ~~gone~~ and snake_case_word.");
        Assert.Contains("<em>em</em>", r.Html);
        Assert.Contains("<strong>strong</strong>", r.Html);
        Assert.Contains("<em><strong>both</strong></em>", r.Html);
        Assert.Contains("<em>under</em>", r.Html);
        Assert.Contains("<code>a &lt; b</code>", r.Html);
        Assert.Contains("<del>gone</del>", r.Html);
        Assert.Contains("snake_case_word", r.Html);
    }

    [Fact]
    public void Markdown_lists_nested_ordered_and_tasks()
    {
        var md = "- one\n- two\n  - nested a\n  - nested b\n- three\n\n3. third\n4. fourth\n\n- [ ] todo\n- [x] done\n";
        var r = MarkdownRenderer.Render(md);
        Assert.Contains("<ul>\n<li>one\n</li>", r.Html);
        Assert.Contains("<li>two\n<ul>\n<li>nested a\n</li>\n<li>nested b\n</li>\n</ul>\n</li>", r.Html);
        Assert.Contains("<ol start=\"3\">", r.Html);
        Assert.Contains("<li>fourth\n</li>", r.Html);
        Assert.Contains("<ul class=\"md-tasks\">", r.Html);
        Assert.Contains("<li class=\"md-task\"><span class=\"md-check\" role=\"checkbox\" aria-checked=\"false\"", r.Html);
        Assert.Contains("<li class=\"md-task done\">", r.Html);
        Assert.DoesNotContain("<input", r.Html);
    }

    [Fact]
    public void Markdown_loose_list_wraps_paragraphs()
    {
        var r = MarkdownRenderer.Render("- a\n\n- b\n");
        Assert.Contains("<li><p>a</p>\n</li>", r.Html);
    }

    [Fact]
    public void Markdown_gfm_table_with_alignment()
    {
        var r = MarkdownRenderer.Render("| Task | Owner | Due |\n|:-----|:-----:|----:|\n| Meds | *RN* | 22:00 |\n| Obs | HCA |\n");
        Assert.Contains("<table>", r.Html);
        Assert.Contains("<th style=\"text-align:left\">Task</th><th style=\"text-align:center\">Owner</th><th style=\"text-align:right\">Due</th>", r.Html);
        Assert.Contains("<td style=\"text-align:center\"><em>RN</em></td>", r.Html);
        Assert.Contains("<td style=\"text-align:center\">HCA</td><td style=\"text-align:right\"></td>", r.Html); // padded
    }

    [Fact]
    public void Markdown_fenced_and_indented_code_are_escaped_verbatim()
    {
        var r = MarkdownRenderer.Render("```js\nif (a < b && c) { x(); }\n<script>alert(1)</script>\n```\n\n    indented <b>\n    line 2\n");
        Assert.Contains("<pre class=\"md-code\"><code class=\"language-js\">if (a &lt; b &amp;&amp; c) { x(); }\n&lt;script&gt;alert(1)&lt;/script&gt;\n</code></pre>", r.Html);
        Assert.Contains("<pre class=\"md-code\"><code>indented &lt;b&gt;\nline 2\n</code></pre>", r.Html);
    }

    [Fact]
    public void Markdown_blockquote_hr_and_hard_break()
    {
        var r = MarkdownRenderer.Render("> quoted **bold**\n> more\n\n---\n\nline one  \nline two\\\nline three");
        Assert.Contains("<blockquote>\n<p>quoted <strong>bold</strong>\nmore</p>\n</blockquote>", r.Html);
        Assert.Contains("<hr />", r.Html);
        Assert.Contains("line one<br />\nline two<br />\nline three", r.Html);
    }

    [Fact]
    public void Markdown_raw_html_is_escaped_never_passed_through()
    {
        var r = MarkdownRenderer.Render("<script>alert('x')</script>\n\nHello <img src=x onerror=alert(1)> <b>bold</b>\n\n<div onclick=\"x()\">block</div>");
        Assert.DoesNotContain("<script", r.Html);
        Assert.DoesNotContain("<img", r.Html);
        Assert.DoesNotContain("<b>", r.Html);
        Assert.DoesNotContain("<div", r.Html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", r.Html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", r.Html);
    }

    [Fact]
    public void Markdown_unsafe_links_render_as_text()
    {
        var r = MarkdownRenderer.Render(
            "[js](javascript:alert(1)) [js2](JaVaScRiPt:alert(1)) [ent](&#106;avascript:alert(1)) [tab](java\tscript:x) " +
            "[vb](vbscript:x) [data](data:text/html,<b>x</b>) [file](file:///c:/x) [rel](docs/a.md) <javascript:alert(1)>");
        Assert.DoesNotContain("href", r.Html);
        Assert.Contains("js", r.Html);
        Assert.Contains("rel", r.Html);
    }

    [Fact]
    public void Markdown_safe_links_anchor_links_and_autolinks()
    {
        var r = MarkdownRenderer.Render("[site](https://example.org/a?b=1&c=2 \"Title\") [mail](mailto:x@y.org) [top](#Before-22:00) <https://a.org> https://b.org/path. <me@x.org>\n\n[ref][r1] and [r1]\n\n[r1]: https://ref.org");
        Assert.Contains("<a href=\"https://example.org/a?b=1&amp;c=2\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"Title\">site</a>", r.Html);
        Assert.Contains("<a href=\"mailto:x@y.org\"", r.Html);
        Assert.Contains("<a href=\"#Before-22:00\" data-anchor-link=\"before-22:00\">top</a>", r.Html);
        Assert.Contains("<a href=\"https://a.org\"", r.Html);
        Assert.Contains("<a href=\"https://b.org/path\"", r.Html); // trailing '.' left out
        Assert.Contains("<a href=\"mailto:me@x.org\"", r.Html);
        Assert.Equal(2, CountOf(r.Html, "href=\"https://ref.org\""));
        Assert.DoesNotContain("[r1]:", r.Html);
    }

    [Fact]
    public void Markdown_images_data_only_by_default()
    {
        const string md = "![dot](data:image/png;base64,iVBORw0KGgo=) ![remote](https://x.org/a.png) ![local](a.png) ![bad](javascript:alert(1))";
        var r = MarkdownRenderer.Render(md);
        Assert.Contains("<img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"dot\"", r.Html);
        Assert.DoesNotContain("x.org/a.png\" alt", r.Html);
        Assert.Equal(1, CountOf(r.Html, "<img"));
        Assert.Equal(3, r.BlockedImages);
        Assert.Equal(1, r.RemoteImages);
        Assert.Contains("class=\"md-img-blocked\"", r.Html);

        var allowed = MarkdownRenderer.Render(md, allowRemoteImages: true);
        Assert.Contains("<img src=\"https://x.org/a.png\" alt=\"remote\"", allowed.Html);
        Assert.Equal(2, allowed.BlockedImages);
    }

    [Fact]
    public void Markdown_plain_text_and_backslash_escapes()
    {
        var r = MarkdownRenderer.Render("# Head\n\n\\*not em\\* &amp; &copy; text");
        Assert.Contains("*not em* &amp; © text", r.Html);
        Assert.Contains("Head", r.PlainText);
        Assert.Contains("*not em* & © text", r.PlainText);
    }

    // ── HtmlSanitizer ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Html_scripts_handlers_and_js_urls_removed()
    {
        var html = "<html><head><title>IC-04</title><script>alert(1)</script><meta http-equiv=\"refresh\" content=\"0;url=https://x\"><base href=\"https://evil/\"><link rel=stylesheet href=\"https://x/a.css\"></head>" +
                   "<body onload=\"x()\"><h1 onclick=\"steal()\">Hand hygiene</h1>" +
                   "<p><a href=\"javascript:alert(1)\">a</a> <a href=\" JaVa&#x09;Script:alert(1)\">b</a> <a href=\"vbscript:x\">c</a> <a href=\"data:text/html,x\">d</a> <a href=\"https://ok.org\" target=\"_top\">ok</a> <a href=\"#s2\">jump</a></p>" +
                   "<SCRIPT SRC=//x></SCRIPT><img src=x onerror=alert(1)><svg><script>alert(2)</script><circle/></svg>" +
                   "<div style=\"background:url(javascript:x)\">styled</div><p style=\"color:red\">red</p><p style=\"width:expr/**/ession(1)\">e</p>" +
                   "<style>@import 'https://x/y.css';</style><style>p{color:blue}</style></body></html>";
        var r = HtmlSanitizer.Sanitize(html);
        var o = r.BodyHtml.ToLowerInvariant();

        Assert.Equal("IC-04", r.Title);
        Assert.DoesNotContain("<script", o);
        Assert.DoesNotContain("alert", o);
        Assert.DoesNotContain("onclick", o);
        Assert.DoesNotContain("onload", o);
        Assert.DoesNotContain("onerror", o);
        Assert.DoesNotContain("javascript", o);
        Assert.DoesNotContain("vbscript", o);
        Assert.DoesNotContain("data:text", o);
        Assert.DoesNotContain("<meta", o);
        Assert.DoesNotContain("<base", o);
        Assert.DoesNotContain("<link", o);
        Assert.DoesNotContain("<svg", o);
        Assert.DoesNotContain("url(", o);
        Assert.DoesNotContain("@import", o);
        Assert.DoesNotContain("ession", o);
        Assert.Contains("<a href=\"https://ok.org\" target=\"_blank\" rel=\"noopener noreferrer\">ok</a>", r.BodyHtml);
        Assert.DoesNotContain("_top", o);
        Assert.Contains("<a href=\"#s2\">jump</a>", r.BodyHtml);
        Assert.Contains("<p style=\"color:red\">red</p>", r.BodyHtml);
        Assert.Contains("<style>p{color:blue}</style>", r.BodyHtml);
        Assert.Contains("<div>styled</div>", r.BodyHtml);

        Assert.Equal(2, r.Blocked.Scripts); // the one inside <svg> goes with the svg (Other)
        Assert.True(r.Blocked.EventHandlers >= 3);
        Assert.True(r.Blocked.UnsafeUrls >= 4);
        Assert.True(r.Blocked.Styles >= 3);
        Assert.True(r.Blocked.Total > 10);
        Assert.Single(r.Outline);
        Assert.Equal("Hand hygiene", r.Outline[0].Title);
    }

    [Fact]
    public void Html_iframes_objects_and_forms_removed()
    {
        var html = "<iframe src=\"https://x\">fallback</iframe><object data=x><param name=a value=b>obj</object><embed src=x>" +
                   "<form action=\"https://x/post\"><label>Name</label><input name=n value=v><button type=submit>Send</button><textarea>secret</textarea><select><option>o</option></select></form>" +
                   "<frameset><frame src=x></frameset><template><p>t</p></template><noscript><p>ns</p></noscript>";
        var r = HtmlSanitizer.Sanitize(html);
        var o = r.BodyHtml.ToLowerInvariant();
        foreach (var bad in new[] { "<iframe", "<object", "<embed", "<param", "<form", "<input", "<button", "<textarea", "<select", "<option", "<frame", "<template", "<noscript", "action=", "fallback", "secret", "send" })
            Assert.DoesNotContain(bad, o);
        Assert.Contains("<label>Name</label>", r.BodyHtml); // form text is unwrapped, not lost
        Assert.True(r.Blocked.Forms >= 5);
        Assert.True(r.Blocked.Embeds >= 4);
    }

    [Fact]
    public void Html_remote_images_blocked_by_default_and_allowed_when_asked()
    {
        var html = "<p><img src=\"https://cdn.x.org/a.png\" alt=\"Poster\" srcset=\"https://x/b.png 2x\"><img src=\"//cdn.x.org/c.png\"><img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"dot\"></p>";

        var blocked = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("cdn.x.org", blocked.BodyHtml);
        Assert.DoesNotContain("srcset", blocked.BodyHtml);
        Assert.Contains("<img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"dot\"", blocked.BodyHtml);
        Assert.Contains("[image: Poster]", blocked.BodyHtml);
        Assert.Equal(2, blocked.Blocked.RemoteImages);

        var allowed = HtmlSanitizer.Sanitize(html, allowRemoteImages: true);
        Assert.Contains("<img src=\"https://cdn.x.org/a.png\" alt=\"Poster\" referrerpolicy=\"no-referrer\">", allowed.BodyHtml);
        Assert.Contains("<img src=\"https://cdn.x.org/c.png\"", allowed.BodyHtml);
        Assert.DoesNotContain("srcset", allowed.BodyHtml);
        Assert.Equal(0, allowed.Blocked.RemoteImages);
    }

    [Fact]
    public void Html_output_is_well_formed_reencoded_and_highlightable()
    {
        var r = HtmlSanitizer.Sanitize("<div><p>Hand &amp; hygiene <b>five moments<p>hand rub</div></b></i><h2 id=\"s2\">Moments</h2><h3>Steps</h3>", highlight: "hand");
        Assert.Equal("<div><p>Hand &amp; hygiene <b>five moments<p>hand rub</p></b></p></div><h2 id=\"s2\">Moments</h2><h3 id=\"chvd-h-1\">Steps</h3>".Replace("Hand", "<mark class=\"chvd-hit\">Hand</mark>").Replace("hand rub", "<mark class=\"chvd-hit\">hand</mark> rub"), r.BodyHtml);
        Assert.Equal(2, r.HighlightCount);
        Assert.Equal(new[] { "s2", "chvd-h-1" }, r.Outline.Select(o => o.Anchor));
        Assert.Contains("Hand & hygiene five moments", r.PlainText);
    }

    [Fact]
    public void Html_srcdoc_puts_csp_first()
    {
        var doc = HtmlSanitizer.BuildSrcDoc("<p>x</p>", allowRemoteImages: false, "body{margin:0}");
        Assert.StartsWith("<!DOCTYPE html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;; style-src &#39;unsafe-inline&#39;; img-src data:\">", doc);
        Assert.Contains("img-src data: https:", HtmlSanitizer.BuildSrcDoc("", true, ""));
    }

    // ── TextViewService ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_decodes_boms_utf8_and_latin1()
    {
        Assert.Equal("héllo", Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("héllo")], out var e1));
        Assert.Equal("UTF-8", e1);
        Assert.Equal("héllo", Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("héllo")], out var e2));
        Assert.Equal("UTF-16 LE", e2);
        Assert.Equal("héllo", Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("héllo")], out var e3));
        Assert.Equal("UTF-16 BE", e3);
        Assert.Equal("வணக்கம்", Decode(Encoding.UTF8.GetBytes("வணக்கம்"), out var e4));
        Assert.Equal("UTF-8", e4);
        Assert.Equal("café", Decode([0x63, 0x61, 0x66, 0xE9], out var e5)); // invalid UTF-8 → Latin-1
        Assert.Equal("Latin-1", e5);
        Assert.Throws<DocumentViewException>(() => Decode([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00], out _));
    }

    [Fact]
    public void Text_splits_lines_and_keeps_text_exactly()
    {
        Assert.Equal(new[] { "a", "b", "", "c" }, TextViewService.SplitLines("a\r\nb\n\rc"));
        Assert.Equal(new[] { "a", "b" }, TextViewService.SplitLines("a\nb\n"));
        Assert.Equal(new[] { "" }, TextViewService.SplitLines(""));

        var path = Path.Combine(Path.GetTempPath(), $"chvd-text-{Guid.NewGuid():N}.txt");
        const string content = "  two leading spaces\r\ndouble  space\ttab \n";
        File.WriteAllText(path, content, new UTF8Encoding(false));
        try
        {
            var doc = new TextViewService().Load(path);
            Assert.Equal(content, doc.Text); // carried through exactly
            Assert.Equal(new[] { "  two leading spaces", "double  space\ttab " }, doc.Lines);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Text_enforces_size_limit_and_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chvd-big-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, new string('x', 2048));
        try
        {
            var ex = Assert.Throws<DocumentViewException>(() => new TextViewService(1024).Load(path));
            Assert.Contains("too large", ex.Message);
            Assert.Equal(2048, new TextViewService(4096).Load(path).Text.Length);
        }
        finally { File.Delete(path); }

        Assert.Throws<DocumentViewException>(() => new TextViewService().Load(path));
        Assert.Equal(20L * 1024 * 1024, TextViewService.DefaultMaxBytes);
    }

    private static string Decode(byte[] bytes, out string enc) => TextViewService.Decode(bytes, out enc);

    private static int CountOf(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
