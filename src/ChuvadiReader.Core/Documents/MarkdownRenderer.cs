using System.Globalization;
using System.Net;
using System.Text;

namespace ChuvadiReader.Core.Documents;

/// <summary>Rendered Markdown: safe HTML, the heading outline (levels 1–3; anchors are the
/// <c>data-anchor</c> values), the number of images not shown (of which <see cref="RemoteImages"/>
/// are http(s) images that <c>allowRemoteImages</c> would show) and the visible text.</summary>
public sealed record MarkdownRenderResult(
    string Html,
    IReadOnlyList<ViewerOutlineEntry> Outline,
    int BlockedImages,
    int RemoteImages,
    string PlainText);

/// <summary>
/// A CommonMark-ish Markdown renderer (D-09), pure BCL. Supports ATX and setext headings (slug
/// anchors), paragraphs, emphasis/strong/strikethrough, code spans, fenced and indented code, block
/// quotes, ordered/unordered/nested lists, task lists, links (inline, reference, autolinks), images,
/// GFM tables, thematic breaks and hard breaks.
/// <para>Safety: raw HTML is always escaped, never passed through. Links keep only http, https,
/// mailto and #anchors (anything else renders as its text). Images render only for <c>data:image/</c>
/// URLs — or http(s) when <c>allowRemoteImages</c> — and otherwise become a placeholder.</para>
/// <para>Output conventions for the viewer: headings carry <c>data-anchor="slug"</c> (no ids, so they
/// never collide with the app's DOM); outline headings also carry <c>data-sec="n"</c> (1-based);
/// in-document links are <c>&lt;a href="#slug" data-anchor-link="slug"&gt;</c>.</para>
/// </summary>
public sealed class MarkdownRenderer
{
    public const string BlockedImageClass = "md-img-blocked";

    private readonly bool _allowRemoteImages;
    private readonly Dictionary<string, (string Url, string? Title)> _refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _slugs = new(StringComparer.Ordinal);
    private readonly List<ViewerOutlineEntry> _outline = new();
    private int _blockedImages;
    private int _remoteImages;

    private MarkdownRenderer(bool allowRemoteImages) => _allowRemoteImages = allowRemoteImages;

    public static MarkdownRenderResult Render(string markdown, bool allowRemoteImages = false)
    {
        var r = new MarkdownRenderer(allowRemoteImages);
        var lines = TextViewService.SplitLines((markdown ?? "").Replace("\0", "�", StringComparison.Ordinal));
        var expanded = new List<string>(lines.Count);
        foreach (var l in lines) expanded.Add(ExpandLeadingTabs(l));

        var blocks = r.ParseBlocks(expanded);
        var sb = new StringBuilder(markdown?.Length ?? 0);
        r.RenderBlocks(blocks, sb, tight: false);
        var html = sb.ToString();
        return new MarkdownRenderResult(html, r._outline, r._blockedImages, r._remoteImages, HtmlSanitizer.ToPlainText(html));
    }

    // ══ block model ══════════════════════════════════════════════════════════════════════════
    private abstract class Block;
    private sealed class HeadingBlock(int level, string text) : Block { public int Level = level; public string Text = text; }
    private sealed class ParaBlock(string text) : Block { public string Text = text; }
    private sealed class CodeBlock(string code, string? lang) : Block { public string Code = code; public string? Lang = lang; }
    private sealed class QuoteBlock(List<Block> children) : Block { public List<Block> Children = children; }
    private sealed class RuleBlock : Block;
    private sealed class ListBlock : Block
    {
        public bool Ordered;
        public int Start = 1;
        public bool Tight = true;
        public List<ListItem> Items = new();
    }
    private sealed class ListItem
    {
        public List<Block> Children = new();
        public bool? Task;
    }
    private sealed class TableBlock : Block
    {
        public List<string> Header = new();
        public List<string?> Aligns = new();
        public List<List<string>> Rows = new();
    }

    // ══ block parsing ════════════════════════════════════════════════════════════════════════
    private List<Block> ParseBlocks(List<string> lines)
    {
        var blocks = new List<Block>();
        int i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (IsBlank(line)) { i++; continue; }

            if (TryFenceOpen(line, out var fenceChar, out var fenceLen, out var fenceIndent, out var info))
            {
                var code = new StringBuilder();
                i++;
                while (i < lines.Count && !IsFenceClose(lines[i], fenceChar, fenceLen))
                {
                    code.Append(StripIndent(lines[i], fenceIndent)).Append('\n');
                    i++;
                }
                if (i < lines.Count) i++; // closing fence
                var lang = info.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                blocks.Add(new CodeBlock(code.ToString(), lang));
                continue;
            }

            if (TryAtx(line, out var level, out var htext)) { blocks.Add(new HeadingBlock(level, htext)); i++; continue; }

            if (IsThematicBreak(line)) { blocks.Add(new RuleBlock()); i++; continue; }

            if (IsQuoteStart(line))
            {
                var inner = new List<string>();
                bool prevBlank = false;
                while (i < lines.Count)
                {
                    var l = lines[i];
                    if (IsQuoteStart(l))
                    {
                        var stripped = StripQuoteMarker(l);
                        inner.Add(stripped);
                        prevBlank = IsBlank(stripped);
                        i++;
                    }
                    else if (!IsBlank(l) && !prevBlank && inner.Count > 0 && !Interrupts(l))
                    {
                        inner.Add(l); // lazy continuation
                        i++;
                    }
                    else break;
                }
                blocks.Add(new QuoteBlock(ParseBlocks(inner)));
                continue;
            }

            if (Indent(line) >= 4)
            {
                var code = new List<string>();
                while (i < lines.Count && (Indent(lines[i]) >= 4 || IsBlank(lines[i])))
                {
                    code.Add(StripIndent(lines[i], 4));
                    i++;
                }
                while (code.Count > 0 && IsBlank(code[^1])) code.RemoveAt(code.Count - 1);
                blocks.Add(new CodeBlock(string.Join('\n', code) + "\n", null));
                continue;
            }

            if (TryListMarker(line, out _))
            {
                blocks.Add(ParseList(lines, ref i));
                continue;
            }

            if (i + 1 < lines.Count && TryTableHeader(line, lines[i + 1], out var table))
            {
                i += 2;
                while (i < lines.Count && !IsBlank(lines[i]) && !Interrupts(lines[i]))
                {
                    table.Rows.Add(FitCells(SplitRow(lines[i]), table.Header.Count));
                    i++;
                }
                blocks.Add(table);
                continue;
            }

            // Paragraph (with link reference definitions and setext headings).
            var para = new List<string>();
            while (i < lines.Count)
            {
                var l = lines[i];
                if (IsBlank(l)) break;
                if (para.Count > 0)
                {
                    if (IsSetextUnderline(l, out var setextLevel))
                    {
                        para = ConsumeRefDefinitions(para);
                        if (para.Count > 0)
                        {
                            blocks.Add(new HeadingBlock(setextLevel, string.Join('\n', para.Select(p => p.Trim()))));
                            para.Clear();
                            i++;
                            goto nextBlock;
                        }
                    }
                    if (Interrupts(l) || (i + 1 < lines.Count && TryTableHeader(l, lines[i + 1], out _))) break;
                }
                para.Add(l);
                i++;
            }
            para = ConsumeRefDefinitions(para);
            if (para.Count > 0) blocks.Add(new ParaBlock(string.Join('\n', para.Select(p => p.TrimStart())).TrimEnd()));
        nextBlock:;
        }
        return blocks;
    }

    private ListBlock ParseList(List<string> lines, ref int i)
    {
        TryListMarker(lines[i], out var first);
        var list = new ListBlock { Ordered = first.Ordered, Start = first.Number };
        List<string>? item = null;
        ListMarker cur = first;
        bool sawBlankBetween = false;
        bool pendingBlank = false;

        while (i < lines.Count)
        {
            var l = lines[i];
            if (TryListMarker(l, out var m) && m.Indent < (item is null ? 4 : cur.ContentOffset)
                && m.Ordered == first.Ordered && m.Delim == first.Delim && !IsThematicBreak(l))
            {
                if (item is not null) { AddItem(list, item); if (pendingBlank) sawBlankBetween = true; }
                item = new List<string>();
                cur = m;
                item.Add(m.Rest);
                pendingBlank = false;
                i++;
                continue;
            }
            if (item is null) break;

            if (IsBlank(l))
            {
                item.Add("");
                pendingBlank = true;
                i++;
                continue;
            }
            if (Indent(l) >= cur.ContentOffset)
            {
                if (pendingBlank && item.Any(x => !IsBlank(x))) sawBlankBetween = true;
                item.Add(StripIndent(l, cur.ContentOffset));
                pendingBlank = false;
                i++;
                continue;
            }
            if (!pendingBlank && !Interrupts(l) && !TryListMarker(l, out _))
            {
                item.Add(l.TrimStart()); // lazy paragraph continuation
                i++;
                continue;
            }
            break;
        }
        if (item is not null) AddItem(list, item);
        // A blank line that only trails the list doesn't make it loose; one between blocks does.
        list.Tight = !sawBlankBetween;
        return list;

        void AddItem(ListBlock l, List<string> raw)
        {
            while (raw.Count > 0 && IsBlank(raw[^1])) raw.RemoveAt(raw.Count - 1);
            var li = new ListItem();
            if (raw.Count > 0)
            {
                var head = raw[0];
                if (head.Length >= 3 && head[0] == '[' && head[2] == ']' && (head.Length == 3 || head[3] == ' ')
                    && head[1] is ' ' or 'x' or 'X')
                {
                    li.Task = head[1] != ' ';
                    raw[0] = head.Length > 4 ? head[4..] : "";
                }
            }
            li.Children = ParseBlocks(raw);
            l.Items.Add(li);
        }
    }

    private List<string> ConsumeRefDefinitions(List<string> para)
    {
        int k = 0;
        while (k < para.Count && TryRefDefinition(para[k])) k++;
        return k == 0 ? para : para.Skip(k).ToList();
    }

    private bool TryRefDefinition(string line)
    {
        var t = line.TrimStart();
        if (Indent(line) > 3 || t.Length < 4 || t[0] != '[') return false;
        int close = t.IndexOf("]:", StringComparison.Ordinal);
        if (close <= 1) return false;
        var label = t[1..close];
        if (label.Contains('[') || label.Contains(']')) return false;
        var rest = t[(close + 2)..].Trim();
        if (rest.Length == 0) return false;

        string url;
        string remainder;
        if (rest[0] == '<')
        {
            int gt = rest.IndexOf('>');
            if (gt < 0) return false;
            url = rest[1..gt];
            remainder = rest[(gt + 1)..].Trim();
        }
        else
        {
            int sp = rest.IndexOfAny([' ', '\t']);
            url = sp < 0 ? rest : rest[..sp];
            remainder = sp < 0 ? "" : rest[sp..].Trim();
        }
        string? title = null;
        if (remainder.Length > 0)
        {
            char o = remainder[0];
            char c = o == '(' ? ')' : o;
            if ((o is '"' or '\'' or '(') && remainder.Length >= 2 && remainder[^1] == c) title = remainder[1..^1];
            else return false;
        }
        var key = NormalizeLabel(label);
        if (key.Length == 0) return false;
        _refs.TryAdd(key, (Unescape(url), title is null ? null : Unescape(title)));
        return true;
    }

    // ── line classifiers ─────────────────────────────────────────────────────────────────────
    private static bool IsBlank(string s)
    {
        foreach (var c in s) if (c != ' ' && c != '\t') return false;
        return true;
    }

    private static int Indent(string s)
    {
        int n = 0;
        while (n < s.Length && s[n] == ' ') n++;
        return n;
    }

    private static string StripIndent(string s, int n)
    {
        int k = 0;
        while (k < n && k < s.Length && s[k] == ' ') k++;
        return s[k..];
    }

    private static string ExpandLeadingTabs(string s)
    {
        if (!s.Contains('\t')) return s;
        var sb = new StringBuilder();
        int col = 0, i = 0;
        for (; i < s.Length && (s[i] == ' ' || s[i] == '\t'); i++)
        {
            if (s[i] == ' ') { sb.Append(' '); col++; }
            else { int w = 4 - col % 4; sb.Append(' ', w); col += w; }
        }
        return sb.Append(s, i, s.Length - i).ToString();
    }

    private static bool Interrupts(string l) =>
        TryAtx(l, out _, out _) || IsThematicBreak(l) || IsQuoteStart(l) || TryFenceOpen(l, out _, out _, out _, out _)
        || (TryListMarker(l, out var m) && m.Rest.Trim().Length > 0 && (!m.Ordered || m.Number == 1));

    private static bool TryAtx(string line, out int level, out string text)
    {
        level = 0; text = "";
        int ind = Indent(line);
        if (ind > 3) return false;
        int i = ind;
        while (i < line.Length && line[i] == '#') i++;
        level = i - ind;
        if (level is < 1 or > 6) return false;
        if (i < line.Length && line[i] != ' ' && line[i] != '\t') return false;
        var t = line[i..].Trim();
        // optional closing sequence: " ###"
        int e = t.Length;
        while (e > 0 && t[e - 1] == '#') e--;
        if (e == 0) t = "";
        else if (e < t.Length && (t[e - 1] == ' ' || t[e - 1] == '\t')) t = t[..e].TrimEnd();
        text = t;
        return true;
    }

    private static bool IsThematicBreak(string line)
    {
        if (Indent(line) > 3) return false;
        char ch = '\0';
        int count = 0;
        foreach (var c in line)
        {
            if (c == ' ' || c == '\t') continue;
            if (c != '-' && c != '*' && c != '_') return false;
            if (ch == '\0') ch = c;
            else if (c != ch) return false;
            count++;
        }
        return count >= 3;
    }

    private static bool IsSetextUnderline(string line, out int level)
    {
        level = 0;
        if (Indent(line) > 3) return false;
        var t = line.Trim();
        if (t.Length == 0) return false;
        if (t.All(c => c == '=')) { level = 1; return true; }
        if (t.All(c => c == '-')) { level = 2; return true; }
        return false;
    }

    private static bool IsQuoteStart(string line) => Indent(line) <= 3 && line.TrimStart().StartsWith('>');

    private static string StripQuoteMarker(string line)
    {
        var t = line.TrimStart();
        t = t[1..];
        if (t.StartsWith(' ')) t = t[1..];
        return t;
    }

    private static bool TryFenceOpen(string line, out char ch, out int len, out int indent, out string info)
    {
        ch = '\0'; len = 0; info = "";
        indent = Indent(line);
        if (indent > 3 || indent >= line.Length) return false;
        char c = line[indent];
        if (c != '`' && c != '~') return false;
        int i = indent;
        while (i < line.Length && line[i] == c) i++;
        len = i - indent;
        if (len < 3) return false;
        info = line[i..].Trim();
        if (c == '`' && info.Contains('`')) return false;
        ch = c;
        return true;
    }

    private static bool IsFenceClose(string line, char ch, int len)
    {
        if (Indent(line) > 3) return false;
        var t = line.Trim();
        if (t.Length < len) return false;
        foreach (var c in t) if (c != ch) return false;
        return true;
    }

    private readonly record struct ListMarker(int Indent, bool Ordered, int Number, char Delim, int ContentOffset, string Rest);

    private static bool TryListMarker(string line, out ListMarker marker)
    {
        marker = default;
        int ind = Indent(line);
        if (ind > 3 || ind >= line.Length) return false;
        int i = ind;
        bool ordered = false;
        int number = 1;
        char delim;
        if (line[i] is '-' or '*' or '+')
        {
            delim = line[i];
            i++;
        }
        else
        {
            int ds = i;
            while (i < line.Length && char.IsAsciiDigit(line[i]) && i - ds < 9) i++;
            if (i == ds || i >= line.Length || (line[i] != '.' && line[i] != ')')) return false;
            number = int.Parse(line.AsSpan(ds, i - ds), NumberStyles.None, CultureInfo.InvariantCulture);
            delim = line[i];
            ordered = true;
            i++;
        }
        int markerEnd = i;
        if (i < line.Length && line[i] != ' ') return false;
        int spaces = 0;
        while (i < line.Length && line[i] == ' ') { spaces++; i++; }
        string rest;
        int contentOffset;
        if (i >= line.Length) { rest = ""; contentOffset = markerEnd + 1; }
        else if (spaces > 4) { contentOffset = markerEnd + 1; rest = line[contentOffset..]; }
        else { contentOffset = markerEnd + spaces; rest = line[i..]; }
        marker = new ListMarker(ind, ordered, number, delim, contentOffset, rest);
        return true;
    }

    // ── tables ───────────────────────────────────────────────────────────────────────────────
    private static bool TryTableHeader(string header, string delimiter, out TableBlock table)
    {
        table = null!;
        if (!header.Contains('|') || Indent(header) > 3 || Indent(delimiter) > 3) return false;
        var d = delimiter.Trim();
        if (d.Length == 0 || !d.Contains('-')) return false;
        foreach (var c in d) if (c is not ('|' or '-' or ':' or ' ' or '\t')) return false;

        var delims = SplitRow(delimiter);
        var heads = SplitRow(header);
        if (delims.Count == 0 || delims.Count != heads.Count) return false;

        var aligns = new List<string?>();
        foreach (var cell in delims)
        {
            var t = cell.Trim();
            if (t.Length == 0 || t.Trim(':').Length == 0 || t.Trim(':').Any(c => c != '-')) return false;
            bool l = t.StartsWith(':'), r = t.EndsWith(':');
            aligns.Add(l && r ? "center" : r ? "right" : l ? "left" : null);
        }
        table = new TableBlock { Header = heads, Aligns = aligns };
        return true;
    }

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool inCode = false;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\' && i + 1 < t.Length && t[i + 1] == '|') { sb.Append('|'); i++; continue; }
            if (c == '`') inCode = !inCode;
            if (c == '|' && !inCode) { cells.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    private static List<string> FitCells(List<string> cells, int n)
    {
        while (cells.Count < n) cells.Add("");
        if (cells.Count > n) cells.RemoveRange(n, cells.Count - n);
        return cells;
    }

    // ══ block rendering ══════════════════════════════════════════════════════════════════════
    private void RenderBlocks(List<Block> blocks, StringBuilder sb, bool tight)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case HeadingBlock h:
                    RenderHeading(h, sb);
                    break;
                case ParaBlock p:
                    if (tight) sb.Append(RenderInline(p.Text)).Append('\n');
                    else sb.Append("<p>").Append(RenderInline(p.Text)).Append("</p>\n");
                    break;
                case CodeBlock c:
                    sb.Append("<pre class=\"md-code\"><code");
                    var lang = SafeLang(c.Lang);
                    if (lang is not null) sb.Append(" class=\"language-").Append(lang).Append('"');
                    sb.Append('>').Append(HtmlSanitizer.Encode(c.Code)).Append("</code></pre>\n");
                    break;
                case QuoteBlock q:
                    sb.Append("<blockquote>\n");
                    RenderBlocks(q.Children, sb, tight: false);
                    sb.Append("</blockquote>\n");
                    break;
                case RuleBlock:
                    sb.Append("<hr />\n");
                    break;
                case ListBlock l:
                    RenderList(l, sb);
                    break;
                case TableBlock t:
                    RenderTable(t, sb);
                    break;
            }
        }
    }

    private void RenderHeading(HeadingBlock h, StringBuilder sb)
    {
        var inner = RenderInline(h.Text);
        var title = HtmlSanitizer.ToPlainText(inner).Replace('\n', ' ').Trim();
        var slug = Slug(title);
        sb.Append("<h").Append(h.Level).Append(" data-anchor=\"").Append(HtmlSanitizer.Encode(slug)).Append('"');
        if (h.Level <= 3)
        {
            _outline.Add(new ViewerOutlineEntry(title.Length > 0 ? title : "(untitled)", h.Level, slug));
            sb.Append(" data-sec=\"").Append(_outline.Count.ToString(CultureInfo.InvariantCulture)).Append('"');
        }
        sb.Append('>').Append(inner).Append("</h").Append(h.Level).Append(">\n");
    }

    private void RenderList(ListBlock l, StringBuilder sb)
    {
        bool isTaskList = l.Items.Any(it => it.Task is not null);
        if (l.Ordered)
        {
            sb.Append("<ol");
            if (l.Start != 1) sb.Append(" start=\"").Append(l.Start.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append(">\n");
        }
        else sb.Append(isTaskList ? "<ul class=\"md-tasks\">\n" : "<ul>\n");

        foreach (var it in l.Items)
        {
            if (it.Task is { } done)
            {
                sb.Append("<li class=\"md-task").Append(done ? " done" : "").Append("\">")
                  .Append("<span class=\"md-check\" role=\"checkbox\" aria-checked=\"").Append(done ? "true" : "false")
                  .Append("\" aria-disabled=\"true\">").Append(done ? "&#x2713;" : "").Append("</span> ");
            }
            else sb.Append("<li>");
            RenderBlocks(it.Children, sb, l.Tight);
            sb.Append("</li>\n");
        }
        sb.Append(l.Ordered ? "</ol>\n" : "</ul>\n");
    }

    private void RenderTable(TableBlock t, StringBuilder sb)
    {
        sb.Append("<div class=\"md-table\"><table>\n<thead><tr>");
        for (int c = 0; c < t.Header.Count; c++) Cell("th", t.Header[c], t.Aligns[c]);
        sb.Append("</tr></thead>\n");
        if (t.Rows.Count > 0)
        {
            sb.Append("<tbody>\n");
            foreach (var row in t.Rows)
            {
                sb.Append("<tr>");
                for (int c = 0; c < row.Count; c++) Cell("td", row[c], t.Aligns[c]);
                sb.Append("</tr>\n");
            }
            sb.Append("</tbody>\n");
        }
        sb.Append("</table></div>\n");

        void Cell(string tag, string text, string? align)
        {
            sb.Append('<').Append(tag);
            if (align is not null) sb.Append(" style=\"text-align:").Append(align).Append('"'); // fixed values only
            sb.Append('>').Append(RenderInline(text)).Append("</").Append(tag).Append('>');
        }
    }

    private static string? SafeLang(string? lang)
    {
        if (string.IsNullOrEmpty(lang)) return null;
        var sb = new StringBuilder();
        foreach (var c in lang)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '#') sb.Append(c == '#' ? "sharp" : c.ToString());
            if (sb.Length >= 32) break;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.ToLowerInvariant())
        {
            var cat = char.GetUnicodeCategory(ch);
            if (char.IsLetterOrDigit(ch) || cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                || ch is '-' or '_')
                sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
        }
        var slug = sb.Length == 0 ? "section" : sb.ToString();
        if (_slugs.TryGetValue(slug, out var n))
        {
            string candidate;
            do candidate = slug + "-" + (++n).ToString(CultureInfo.InvariantCulture);
            while (_slugs.ContainsKey(candidate));
            _slugs[slug] = n;
            _slugs[candidate] = 0;
            return candidate;
        }
        _slugs[slug] = 0;
        return slug;
    }

    // ══ inlines ══════════════════════════════════════════════════════════════════════════════
    private enum NodeKind { Text, Html, Delim }

    private sealed class Node
    {
        public NodeKind Kind;
        public string S = "";
        public char C;
        public int Count, Orig;
        public bool CanOpen, CanClose;
    }

    private string RenderInline(string text, bool inLink = false)
    {
        var nodes = new List<Node>();
        var buf = new StringBuilder();

        void Flush()
        {
            if (buf.Length == 0) return;
            nodes.Add(new Node { Kind = NodeKind.Text, S = buf.ToString() });
            buf.Clear();
        }
        void Html(string html)
        {
            Flush();
            nodes.Add(new Node { Kind = NodeKind.Html, S = html });
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 < text.Length && text[i + 1] == '\n') { TrimTrailingSpaces(buf); Html("<br />\n"); i += 2; continue; }
                    if (i + 1 < text.Length && char.IsAsciiLetterOrDigit(text[i + 1]) == false && IsAsciiPunct(text[i + 1]))
                    {
                        buf.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    buf.Append('\\');
                    i++;
                    continue;

                case '`':
                {
                    int n = Run(text, i, '`');
                    int close = FindBacktickRun(text, i + n, n);
                    if (close < 0) { buf.Append('`', n); i += n; continue; }
                    var code = text[(i + n)..close].Replace('\n', ' ');
                    if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0) code = code[1..^1];
                    Html("<code>" + HtmlSanitizer.Encode(code) + "</code>");
                    i = close + n;
                    continue;
                }

                case '<':
                {
                    int gt = text.IndexOf('>', i + 1);
                    if (gt > i + 1 && !inLink)
                    {
                        var inner = text[(i + 1)..gt];
                        if (!inner.Any(ch => ch is ' ' or '<' or '\n' or '\t'))
                        {
                            if (inner.Contains('@') && !inner.Contains(':') && IsEmail(inner))
                            {
                                Html(LinkHtml("mailto:" + inner, null, HtmlSanitizer.Encode(inner)) ?? HtmlSanitizer.Encode(inner));
                                i = gt + 1;
                                continue;
                            }
                            if (HtmlSanitizer.SchemeOf(inner) is { } sch && inner.IndexOf(':') > 1)
                            {
                                var lh = sch is "http" or "https" or "mailto" ? LinkHtml(inner, null, HtmlSanitizer.Encode(inner)) : null;
                                Html(lh ?? HtmlSanitizer.Encode(inner));
                                i = gt + 1;
                                continue;
                            }
                        }
                    }
                    buf.Append('<'); // raw HTML is escaped, never passed through
                    i++;
                    continue;
                }

                case '!' when i + 1 < text.Length && text[i + 1] == '[':
                {
                    if (TryLinkOrImage(text, i + 1, isImage: true, out var html, out var end)) { Html(html); i = end; continue; }
                    buf.Append('!');
                    i++;
                    continue;
                }

                case '[':
                {
                    if (!inLink && TryLinkOrImage(text, i, isImage: false, out var html, out var end)) { Html(html); i = end; continue; }
                    buf.Append('[');
                    i++;
                    continue;
                }

                case '*':
                case '_':
                case '~':
                {
                    int n = Run(text, i, c);
                    if (c == '~' && n != 2) { buf.Append('~', n); i += n; continue; }
                    char prev = i > 0 ? text[i - 1] : '\n';
                    char next = i + n < text.Length ? text[i + n] : '\n';
                    bool prevWs = char.IsWhiteSpace(prev), nextWs = char.IsWhiteSpace(next);
                    bool prevP = IsPunct(prev), nextP = IsPunct(next);
                    bool left = !nextWs && (!nextP || prevWs || prevP);
                    bool right = !prevWs && (!prevP || nextWs || nextP);
                    bool open = c == '_' ? left && (!right || prevP) : left;
                    bool close = c == '_' ? right && (!left || nextP) : right;
                    Flush();
                    nodes.Add(new Node { Kind = NodeKind.Delim, C = c, Count = n, Orig = n, CanOpen = open, CanClose = close });
                    i += n;
                    continue;
                }

                case '\n':
                {
                    int spaces = 0;
                    for (int k = buf.Length - 1; k >= 0 && buf[k] == ' '; k--) spaces++;
                    TrimTrailingSpaces(buf);
                    if (spaces >= 2) Html("<br />\n");
                    else buf.Append('\n');
                    i++;
                    while (i < text.Length && text[i] == ' ') i++;
                    continue;
                }

                case '&':
                {
                    int semi = text.IndexOf(';', i + 1);
                    if (semi > i + 1 && semi - i <= 33)
                    {
                        var ent = text[i..(semi + 1)];
                        var dec = WebUtility.HtmlDecode(ent);
                        if (dec != ent && IsEntityName(ent))
                        {
                            buf.Append(dec.Replace("\0", "�", StringComparison.Ordinal));
                            i = semi + 1;
                            continue;
                        }
                    }
                    buf.Append('&');
                    i++;
                    continue;
                }

                case 'h' when !inLink && (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] is '(' or '*' or '_' or '~')
                              && (string.CompareOrdinal(text, i, "https://", 0, 8) == 0 || string.CompareOrdinal(text, i, "http://", 0, 7) == 0):
                {
                    int e = i;
                    while (e < text.Length && !char.IsWhiteSpace(text[e]) && text[e] != '<') e++;
                    // strip trailing punctuation and unbalanced ')'
                    while (e > i)
                    {
                        char t = text[e - 1];
                        if (t is '.' or ',' or ':' or ';' or '!' or '?' or '"' or '\'' or '*' or '_' or '~') { e--; continue; }
                        if (t == ')' && text[i..e].Count(x => x == '(') < text[i..e].Count(x => x == ')')) { e--; continue; }
                        break;
                    }
                    var url = text[i..e];
                    var lh = url.Length > 8 ? LinkHtml(url, null, HtmlSanitizer.Encode(url)) : null;
                    if (lh is null) { buf.Append(c); i++; continue; }
                    Html(lh);
                    i = e;
                    continue;
                }

                default:
                    buf.Append(c);
                    i++;
                    continue;
            }
        }
        Flush();
        ProcessEmphasis(nodes);

        var sb = new StringBuilder();
        foreach (var n in nodes) AppendNode(sb, n);
        return sb.ToString();
    }

    private static void ProcessEmphasis(List<Node> nodes)
    {
        for (int ci = 0; ci < nodes.Count; ci++)
        {
            var closer = nodes[ci];
            if (closer.Kind != NodeKind.Delim || !closer.CanClose || closer.Count == 0) continue;

            for (int oi = ci - 1; oi >= 0; oi--)
            {
                var op = nodes[oi];
                if (op.Kind != NodeKind.Delim || op.C != closer.C || !op.CanOpen || op.Count == 0) continue;
                if (closer.C != '~' && (op.CanClose || closer.CanOpen)
                    && (op.Orig + closer.Orig) % 3 == 0 && !(op.Orig % 3 == 0 && closer.Orig % 3 == 0))
                    continue;

                int use = closer.C == '~' ? 2 : (op.Count >= 2 && closer.Count >= 2 ? 2 : 1);
                string tag = closer.C == '~' ? "del" : use == 2 ? "strong" : "em";
                var sb = new StringBuilder();
                sb.Append('<').Append(tag).Append('>');
                for (int k = oi + 1; k < ci; k++) AppendNode(sb, nodes[k]);
                sb.Append("</").Append(tag).Append('>');

                op.Count -= use;
                closer.Count -= use;
                nodes.RemoveRange(oi + 1, ci - oi - 1);
                nodes.Insert(oi + 1, new Node { Kind = NodeKind.Html, S = sb.ToString() });
                int closerIdx = oi + 2;
                if (op.Count == 0) { nodes.RemoveAt(oi); closerIdx--; }
                if (closer.Count == 0) { nodes.RemoveAt(closerIdx); ci = closerIdx - 1; }
                else ci = closerIdx - 1; // revisit the same closer with its remaining count
                break;
            }
        }
    }

    private static void AppendNode(StringBuilder sb, Node n)
    {
        switch (n.Kind)
        {
            case NodeKind.Text: sb.Append(HtmlSanitizer.Encode(n.S)); break;
            case NodeKind.Html: sb.Append(n.S); break;
            case NodeKind.Delim: sb.Append(n.C, n.Count); break;
        }
    }

    private bool TryLinkOrImage(string text, int open, bool isImage, out string html, out int end)
    {
        html = ""; end = 0;
        int close = FindClosingBracket(text, open);
        if (close < 0) return false;
        var label = text[(open + 1)..close];

        string? url = null, title = null;
        int after = close + 1;
        if (after < text.Length && text[after] == '(' && TryInlineDestination(text, after, out var u, out var t, out var e))
        {
            url = u; title = t; end = e;
        }
        else
        {
            string key;
            if (after < text.Length && text[after] == '[')
            {
                int rc = text.IndexOf(']', after + 1);
                if (rc < 0) return false;
                key = rc == after + 1 ? label : text[(after + 1)..rc];
                end = rc + 1;
            }
            else
            {
                key = label;
                end = close + 1;
            }
            if (!_refs.TryGetValue(NormalizeLabel(key), out var def)) return false;
            url = def.Url; title = def.Title;
        }

        if (isImage)
        {
            var alt = HtmlSanitizer.ToPlainText(RenderInline(label, inLink: true)).Replace('\n', ' ').Trim();
            html = ImageHtml(url ?? "", title, alt);
            end = end <= open ? close + 1 : end;
            return true;
        }

        var inner = RenderInline(label, inLink: true);
        html = LinkHtml(url ?? "", title, inner) ?? inner; // unsafe or relative link → its text
        return true;
    }

    private static int FindClosingBracket(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\') { i++; continue; }
            if (c == '`')
            {
                int n = Run(text, i, '`');
                int cl = FindBacktickRun(text, i + n, n);
                if (cl >= 0) { i = cl + n - 1; continue; }
                i += n - 1;
                continue;
            }
            if (c == '[') depth++;
            else if (c == ']' && --depth == 0) return i;
        }
        return -1;
    }

    private static bool TryInlineDestination(string text, int paren, out string url, out string? title, out int end)
    {
        url = ""; title = null; end = 0;
        int i = paren + 1;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        var sb = new StringBuilder();
        if (i < text.Length && text[i] == '<')
        {
            int gt = text.IndexOf('>', i + 1);
            if (gt < 0 || text.AsSpan(i + 1, gt - i - 1).Contains('\n')) return false;
            sb.Append(text, i + 1, gt - i - 1);
            i = gt + 1;
        }
        else
        {
            int depth = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) break;
                if (c == '\\' && i + 1 < text.Length && IsAsciiPunct(text[i + 1])) { sb.Append(c).Append(text[i + 1]); i += 2; continue; }
                if (c == '(') depth++;
                else if (c == ')') { if (depth == 0) break; depth--; }
                sb.Append(c);
                i++;
            }
        }
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i < text.Length && text[i] is '"' or '\'' or '(')
        {
            char o = text[i];
            char cl = o == '(' ? ')' : o;
            int k = i + 1;
            var tb = new StringBuilder();
            while (k < text.Length && text[k] != cl)
            {
                if (text[k] == '\\' && k + 1 < text.Length) { tb.Append(text[k + 1]); k += 2; continue; }
                tb.Append(text[k]);
                k++;
            }
            if (k >= text.Length) return false;
            title = tb.ToString();
            i = k + 1;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        }
        if (i >= text.Length || text[i] != ')') return false;
        url = Unescape(sb.ToString());
        end = i + 1;
        return true;
    }

    /// <summary>Returns the anchor HTML, or null when the URL isn't http, https, mailto or #anchor.</summary>
    private static string? LinkHtml(string rawUrl, string? title, string innerHtml)
    {
        var url = WebUtility.HtmlDecode(rawUrl).Trim();
        if (url.Any(char.IsControl)) return null;
        string? titleAttr = string.IsNullOrEmpty(title) ? null : HtmlSanitizer.Encode(title);

        if (url.StartsWith('#'))
        {
            var anchor = url[1..];
            if (anchor.Length == 0) return null;
            var dec = Uri.UnescapeDataString(anchor).ToLowerInvariant();
            return "<a href=\"#" + HtmlSanitizer.Encode(anchor) + "\" data-anchor-link=\"" + HtmlSanitizer.Encode(dec) + "\""
                   + (titleAttr is null ? "" : " title=\"" + titleAttr + "\"") + ">" + innerHtml + "</a>";
        }

        var scheme = HtmlSanitizer.SchemeOf(url);
        if (scheme is not ("http" or "https" or "mailto")) return null;
        if (url.Any(char.IsWhiteSpace)) url = url.Replace(" ", "%20", StringComparison.Ordinal);
        if (url.Any(char.IsWhiteSpace)) return null;
        return "<a href=\"" + HtmlSanitizer.Encode(url) + "\" target=\"_blank\" rel=\"noopener noreferrer\""
               + (titleAttr is null ? "" : " title=\"" + titleAttr + "\"") + ">" + innerHtml + "</a>";
    }

    private string ImageHtml(string rawUrl, string? title, string alt)
    {
        var url = WebUtility.HtmlDecode(rawUrl).Trim();
        var scheme = HtmlSanitizer.SchemeOf(url);
        bool controls = url.Any(char.IsControl);
        bool ok = !controls && (
            (scheme == "data" && url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && !url.Any(char.IsWhiteSpace))
            || (_allowRemoteImages && scheme is "http" or "https" && !url.Any(char.IsWhiteSpace)));

        if (!ok)
        {
            _blockedImages++;
            bool remote = scheme is "http" or "https";
            if (remote && !controls) _remoteImages++;
            var label = alt.Length > 0 ? "image: " + alt : "image";
            return "<span class=\"" + BlockedImageClass + "\" title=\""
                   + HtmlSanitizer.Encode(remote ? "Remote image not loaded: " + url : "Image not shown")
                   + "\">[" + HtmlSanitizer.Encode(label) + "]</span>";
        }
        return "<img src=\"" + HtmlSanitizer.Encode(url) + "\" alt=\"" + HtmlSanitizer.Encode(alt) + "\""
               + (string.IsNullOrEmpty(title) ? "" : " title=\"" + HtmlSanitizer.Encode(title) + "\"")
               + " referrerpolicy=\"no-referrer\" />";
    }

    // ── small helpers ────────────────────────────────────────────────────────────────────────
    private static int Run(string s, int i, char c)
    {
        int n = 0;
        while (i + n < s.Length && s[i + n] == c) n++;
        return n;
    }

    private static int FindBacktickRun(string s, int from, int n)
    {
        int i = from;
        while (i < s.Length)
        {
            int k = s.IndexOf('`', i);
            if (k < 0) return -1;
            int r = Run(s, k, '`');
            if (r == n) return k;
            i = k + r;
        }
        return -1;
    }

    private static void TrimTrailingSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
    }

    private static bool IsAsciiPunct(char c) => c < 128 && (char.IsPunctuation(c) || char.IsSymbol(c));

    private static bool IsPunct(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    private static bool IsEntityName(string ent)
    {
        // "&name;", "&#123;" or "&#x1F;"
        var body = ent[1..^1];
        if (body.Length == 0) return false;
        if (body[0] == '#')
        {
            if (body.Length > 1 && (body[1] == 'x' || body[1] == 'X')) return body.Length > 2 && body[2..].All(char.IsAsciiHexDigit);
            return body.Length > 1 && body[1..].All(char.IsAsciiDigit);
        }
        return body.All(char.IsAsciiLetterOrDigit);
    }

    private static bool IsEmail(string s)
    {
        int at = s.IndexOf('@');
        if (at <= 0 || at != s.LastIndexOf('@') || at == s.Length - 1) return false;
        var domain = s[(at + 1)..];
        return domain.Contains('.') && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '@');
    }

    private static string NormalizeLabel(string label)
    {
        var sb = new StringBuilder();
        foreach (var c in label.Trim())
        {
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
            else sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant().ToLowerInvariant();
    }

    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && IsAsciiPunct(s[i + 1])) { sb.Append(s[i + 1]); i++; }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
