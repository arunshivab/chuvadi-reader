using System.Globalization;
using System.Net;
using System.Text;

namespace ChuvadiReader.Core.Documents;

/// <summary>What the sanitizer removed from an untrusted HTML file (shown in the HTML banner).</summary>
public sealed record HtmlBlockedCounts(
    int Scripts, int EventHandlers, int UnsafeUrls, int Embeds, int Forms,
    int RemoteImages, int Styles, int Other)
{
    public int Total => Scripts + EventHandlers + UnsafeUrls + Embeds + Forms + RemoteImages + Styles + Other;
}

/// <summary>Sanitized body HTML (safe to place in a sandboxed srcdoc), the document title, an h1–h3
/// outline (anchors are element ids inside the body), what was blocked, the visible text and — when a
/// highlight was asked for — how many matches were wrapped in <c>&lt;mark class="chvd-hit"&gt;</c>.</summary>
public sealed record HtmlSanitizeResult(
    string BodyHtml,
    string? Title,
    IReadOnlyList<ViewerOutlineEntry> Outline,
    HtmlBlockedCounts Blocked,
    string PlainText,
    int HighlightCount);

/// <summary>
/// Allow-list sanitizer for untrusted .html files (D-09). Pure BCL: a tolerant tokenizer feeds an
/// element/attribute allow-list and the output is re-serialised (always well-formed, every text node
/// and attribute re-encoded), so nothing from the source is passed through verbatim.
/// <para>Removed: script, iframe/frame/object/embed/applet, form controls, meta (incl. refresh), base,
/// link, svg/math, template, media; every <c>on*</c> attribute; <c>javascript:</c>/<c>vbscript:</c>/
/// <c>data:</c> URLs (except <c>data:image/</c> on img); <c>srcset</c>; style attributes or style
/// elements containing <c>url(</c>, <c>expression</c>, <c>@import</c> and similar; remote
/// <c>img src</c> unless <paramref name="allowRemoteImages"/>. Forms are unwrapped (their text stays).</para>
/// This is one layer: the viewer also renders the result in <c>&lt;iframe sandbox=""&gt;</c> with a
/// CSP meta (see <see cref="BuildSrcDoc"/>).
/// </summary>
public static class HtmlSanitizer
{
    public const string HitClass = "chvd-hit";
    public const string ImagePlaceholderClass = "chvd-img-ph";

    /// <summary>The CSP placed first in the srcdoc. Remote images add <c>https:</c> to img-src only.</summary>
    public static string ContentSecurityPolicy(bool allowRemoteImages) =>
        "default-src 'none'; style-src 'unsafe-inline'; img-src data:" + (allowRemoteImages ? " https:" : "");

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br",
        "caption", "center", "cite", "code", "col", "colgroup", "dd", "del", "details", "dfn", "div",
        "dl", "dt", "em", "fieldset", "figcaption", "figure", "font", "footer", "h1", "h2", "h3", "h4",
        "h5", "h6", "header", "hgroup", "hr", "i", "img", "ins", "kbd", "label", "legend", "li", "main",
        "mark", "nav", "ol", "p", "pre", "q", "rp", "rt", "ruby", "s", "samp", "section", "small", "span",
        "strike", "strong", "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time",
        "tr", "tt", "u", "ul", "var", "wbr",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param",
        "source", "track", "wbr", "frame", "basefont", "bgsound", "image", "isindex",
    };

    // Tokenizer treats these as raw text up to their end tag.
    private static readonly HashSet<string> RawText = new(StringComparer.Ordinal)
    {
        "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext",
    };

    // Dropped together with everything inside them (category for the banner counts).
    private static readonly Dictionary<string, Category> DropWithContent = new(StringComparer.Ordinal)
    {
        ["script"] = Category.Script,
        ["noscript"] = Category.Other,
        ["iframe"] = Category.Embed, ["frame"] = Category.Embed, ["frameset"] = Category.Embed,
        ["object"] = Category.Embed, ["embed"] = Category.Embed, ["applet"] = Category.Embed,
        ["portal"] = Category.Embed, ["param"] = Category.Embed,
        ["noembed"] = Category.Other, ["noframes"] = Category.Other,
        ["textarea"] = Category.Form, ["select"] = Category.Form, ["button"] = Category.Form,
        ["input"] = Category.Form, ["option"] = Category.Form, ["optgroup"] = Category.Form,
        ["datalist"] = Category.Form, ["keygen"] = Category.Form, ["output"] = Category.Form,
        ["svg"] = Category.Other, ["math"] = Category.Other, ["template"] = Category.Other,
        ["audio"] = Category.Embed, ["video"] = Category.Embed, ["source"] = Category.Embed,
        ["track"] = Category.Embed, ["canvas"] = Category.Other, ["dialog"] = Category.Other,
        ["meta"] = Category.Other, ["link"] = Category.Other, ["base"] = Category.Other,
        ["xmp"] = Category.Other, ["plaintext"] = Category.Other, ["map"] = Category.Other,
        ["area"] = Category.Other, ["image"] = Category.Other, ["bgsound"] = Category.Other,
        ["isindex"] = Category.Form,
    };

    // Unwrapped silently (structure only) or counted (form).
    private static readonly HashSet<string> Unwrap = new(StringComparer.Ordinal) { "html", "head", "body", "form" };

    private static readonly HashSet<string> Block = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "br", "caption", "center", "dd", "details", "div",
        "dl", "dt", "fieldset", "figcaption", "figure", "footer", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hgroup", "hr", "legend", "li", "main", "nav", "ol", "p", "pre", "section", "summary",
        "table", "tbody", "tfoot", "thead", "tr", "ul",
    };

    private static readonly HashSet<string> GlobalAttrs = new(StringComparer.Ordinal)
    {
        "id", "class", "title", "lang", "dir", "style", "role", "align", "valign", "width", "height",
        "bgcolor", "color", "border", "hidden", "translate",
    };

    private static readonly Dictionary<string, string[]> ElementAttrs = new(StringComparer.Ordinal)
    {
        ["a"] = ["href", "name"],
        ["img"] = ["src", "alt"],
        ["td"] = ["colspan", "rowspan", "scope", "headers", "abbr", "nowrap"],
        ["th"] = ["colspan", "rowspan", "scope", "headers", "abbr", "nowrap"],
        ["ol"] = ["start", "type", "reversed"],
        ["ul"] = ["type"],
        ["li"] = ["value", "type"],
        ["col"] = ["span"],
        ["colgroup"] = ["span"],
        ["table"] = ["cellpadding", "cellspacing", "summary", "rules", "frame"],
        ["font"] = ["face", "size"],
        ["time"] = ["datetime"],
        ["del"] = ["datetime"],
        ["ins"] = ["datetime"],
        ["details"] = ["open"],
        ["bdo"] = ["dir"],
    };

    private enum Category { None, Script, Embed, Form, Other }

    /// <summary>Sanitizes untrusted HTML. <paramref name="highlight"/> (optional, case-insensitive)
    /// wraps matches in visible text with <c>&lt;mark class="chvd-hit"&gt;</c>.</summary>
    public static HtmlSanitizeResult Sanitize(string html, bool allowRemoteImages = false, string? highlight = null)
    {
        var s = new State(html ?? "", allowRemoteImages, string.IsNullOrEmpty(highlight) ? null : highlight);
        s.Run();
        return s.Result();
    }

    /// <summary>Wraps sanitized body HTML in a complete document whose first element in head is the
    /// CSP meta. <paramref name="baseCss"/> must be trusted (the viewer's own styles).</summary>
    public static string BuildSrcDoc(string sanitizedBody, bool allowRemoteImages, string baseCss, string? title = null)
    {
        var sb = new StringBuilder(sanitizedBody.Length + baseCss.Length + 512);
        sb.Append("<!DOCTYPE html><html><head>");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
          .Append(Encode(ContentSecurityPolicy(allowRemoteImages))).Append("\">");
        sb.Append("<meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\">");
        if (!string.IsNullOrEmpty(title)) sb.Append("<title>").Append(Encode(title)).Append("</title>");
        sb.Append("<style>").Append(baseCss.Replace("</", "<\\/", StringComparison.Ordinal)).Append("</style>");
        sb.Append("</head><body>").Append(sanitizedBody).Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Visible text of trusted, well-formed HTML (e.g. the Markdown renderer's output).</summary>
    public static string ToPlainText(string safeHtml) => Sanitize(safeHtml, allowRemoteImages: true).PlainText;

    /// <summary>Minimal attribute/text encoding (&amp; &lt; &gt; " '); non-ASCII text stays as-is.</summary>
    public static string Encode(string s)
    {
        if (s.AsSpan().IndexOfAny("&<>\"'") < 0) return s;
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>True when a CSS fragment (style attribute or element) can load or execute anything.</summary>
    public static bool IsDangerousCss(string css)
    {
        // Comments and whitespace can split keywords ("ur/**/l(", "expr ession"); escapes can encode them.
        var sb = new StringBuilder(css.Length);
        for (int i = 0; i < css.Length; i++)
        {
            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 1;
                continue;
            }
            char c = css[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        var t = sb.ToString();
        return t.Contains('\\')
            || t.Contains("url(", StringComparison.Ordinal)
            || t.Contains("expression", StringComparison.Ordinal)
            || t.Contains("@import", StringComparison.Ordinal)
            || t.Contains("behavior", StringComparison.Ordinal)
            || t.Contains("binding", StringComparison.Ordinal)
            || t.Contains("javascript:", StringComparison.Ordinal)
            || t.Contains("vbscript:", StringComparison.Ordinal)
            || t.Contains("image-set(", StringComparison.Ordinal)
            || t.Contains("image(", StringComparison.Ordinal)
            || t.Contains("src(", StringComparison.Ordinal)
            || t.Contains("element(", StringComparison.Ordinal)
            || t.Contains("@font-face", StringComparison.Ordinal)
            || t.Contains("</", StringComparison.Ordinal)
            || t.Contains("<!--", StringComparison.Ordinal);
    }

    /// <summary>Lower-case scheme of a URL after removing whitespace/control characters (which
    /// browsers ignore inside schemes), or null if it has none.</summary>
    internal static string? SchemeOf(string url)
    {
        var sb = new StringBuilder();
        foreach (var c in url)
        {
            if (c <= ' ' || char.IsControl(c) || c == '­' || c == '​' || c == '﻿') continue;
            if (c == ':') return sb.Length > 0 && IsSchemeText(sb) ? sb.ToString().ToLowerInvariant() : null;
            if (c == '/' || c == '?' || c == '#') return null;
            sb.Append(c);
        }
        return null;
    }

    private static bool IsSchemeText(StringBuilder sb)
    {
        if (!char.IsAsciiLetter(sb[0])) return false;
        for (int i = 1; i < sb.Length; i++)
        {
            char c = sb[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.') return false;
        }
        return true;
    }

    private static bool HasControlChars(string s)
    {
        foreach (var c in s) if (char.IsControl(c)) return true;
        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    private sealed class State
    {
        private readonly string _src;
        private readonly bool _remote;
        private readonly string? _hl;
        private readonly StringBuilder _out;
        private readonly StringBuilder _text = new();
        private readonly List<string> _stack = new();
        private readonly List<ViewerOutlineEntry> _outline = new();
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private int _pos;
        private string? _title;
        private int _hits, _pre;
        private int _scripts, _handlers, _urls, _embeds, _forms, _remoteImgs, _styles, _other;
        private int _genId;

        // Text of the h1–h3 currently open (for the outline).
        private int _headingDepth = -1;
        private string? _headingId;
        private int _headingLevel;
        private StringBuilder? _headingText;

        public State(string src, bool remote, string? hl)
        {
            _src = src;
            _remote = remote;
            _hl = hl;
            _out = new StringBuilder(src.Length);
        }

        public HtmlSanitizeResult Result()
        {
            while (_stack.Count > 0) Close(_stack.Count - 1);
            return new HtmlSanitizeResult(
                _out.ToString(),
                string.IsNullOrWhiteSpace(_title) ? null : Collapse(_title).Trim(),
                _outline,
                new HtmlBlockedCounts(_scripts, _handlers, _urls, _embeds, _forms, _remoteImgs, _styles, _other),
                NormalizeText(_text.ToString()),
                _hits);
        }

        public void Run()
        {
            while (_pos < _src.Length)
            {
                int lt = _src.IndexOf('<', _pos);
                if (lt < 0) { EmitText(_src[_pos..]); break; }
                if (lt > _pos) EmitText(_src[_pos..lt]);
                _pos = lt;
                if (!TryMarkup()) { EmitText("<"); _pos++; }
            }
        }

        // ── tokenizer ─────────────────────────────────────────────────────────────────────────
        private bool TryMarkup()
        {
            var s = _src;
            int p = _pos;
            if (p + 1 >= s.Length) return false;
            char n = s[p + 1];

            if (n == '!')
            {
                if (string.CompareOrdinal(s, p, "<!--", 0, 4) == 0)
                {
                    int end = s.IndexOf("-->", p + 4, StringComparison.Ordinal);
                    _pos = end < 0 ? s.Length : end + 3;
                    return true;
                }
                if (string.CompareOrdinal(s, p, "<![CDATA[", 0, 9) == 0)
                {
                    int end = s.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                    _pos = end < 0 ? s.Length : end + 3;
                    return true;
                }
                int gt = s.IndexOf('>', p + 2);
                _pos = gt < 0 ? s.Length : gt + 1; // doctype / bogus comment
                return true;
            }
            if (n == '?')
            {
                int gt = s.IndexOf('>', p + 2);
                _pos = gt < 0 ? s.Length : gt + 1;
                return true;
            }
            if (n == '/')
            {
                if (p + 2 >= s.Length || !char.IsAsciiLetter(s[p + 2]))
                {
                    if (p + 2 < s.Length && s[p + 2] == '>') { _pos = p + 3; return true; }
                    int g = s.IndexOf('>', p + 2); // bogus comment
                    _pos = g < 0 ? s.Length : g + 1;
                    return true;
                }
                int q = p + 2;
                while (q < s.Length && IsNameChar(s[q])) q++;
                var name = s[(p + 2)..q].ToLowerInvariant();
                int gt = s.IndexOf('>', q);
                _pos = gt < 0 ? s.Length : gt + 1;
                EndTag(name);
                return true;
            }
            if (!char.IsAsciiLetter(n)) return false;

            int i = p + 1;
            while (i < s.Length && IsNameChar(s[i])) i++;
            var tag = s[(p + 1)..i].ToLowerInvariant();
            var attrs = new List<KeyValuePair<string, string>>();
            bool selfClose = false;
            while (true)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '/'))
                {
                    if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '>') selfClose = true;
                    i++;
                }
                if (i >= s.Length) break;
                if (s[i] == '>') { i++; break; }
                int ns = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '/' && s[i] != '>' && (s[i] != '=' || i == ns)) i++;
                var an = s[ns..i].ToLowerInvariant();
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                string av = "";
                if (i < s.Length && s[i] == '=')
                {
                    i++;
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                    {
                        char qch = s[i];
                        int ve = s.IndexOf(qch, i + 1);
                        if (ve < 0) ve = s.Length;
                        av = s[(i + 1)..ve];
                        i = Math.Min(s.Length, ve + 1);
                    }
                    else
                    {
                        int vs = i;
                        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '>') i++;
                        av = s[vs..i];
                    }
                }
                if (an.Length > 0) attrs.Add(new(an, WebUtility.HtmlDecode(av)));
            }
            _pos = i;

            string? raw = null;
            if (RawText.Contains(tag) && !selfClose)
            {
                if (tag == "plaintext") { raw = s[_pos..]; _pos = s.Length; }
                else
                {
                    int end = IndexOfEndTag(s, _pos, tag);
                    raw = s[_pos..(end < 0 ? s.Length : end)];
                    if (end < 0) _pos = s.Length;
                    else
                    {
                        int gt = s.IndexOf('>', end);
                        _pos = gt < 0 ? s.Length : gt + 1;
                    }
                }
            }
            StartTag(tag, attrs, selfClose, raw);
            return true;
        }

        private static int IndexOfEndTag(string s, int from, string tag)
        {
            int i = from;
            while (true)
            {
                int k = s.IndexOf("</", i, StringComparison.Ordinal);
                if (k < 0) return -1;
                if (k + 2 + tag.Length <= s.Length
                    && string.Compare(s, k + 2, tag, 0, tag.Length, StringComparison.OrdinalIgnoreCase) == 0
                    && (k + 2 + tag.Length == s.Length || !IsNameChar(s[k + 2 + tag.Length])))
                    return k;
                i = k + 2;
            }
        }

        private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.';

        // ── tree ──────────────────────────────────────────────────────────────────────────────
        private int _skipDepth;      // >0 while inside a dropped element
        private string? _skipTag;

        private void StartTag(string tag, List<KeyValuePair<string, string>> attrs, bool selfClose, string? raw)
        {
            if (_skipDepth > 0)
            {
                if (tag == _skipTag && !selfClose && !Void.Contains(tag) && raw is null) _skipDepth++;
                return;
            }

            if (tag == "title")
            {
                _title ??= WebUtility.HtmlDecode(raw ?? "");
                return;
            }

            if (tag == "style")
            {
                var css = raw ?? "";
                if (IsDangerousCss(css)) { _styles++; return; }
                if (css.Trim().Length > 0)
                    _out.Append("<style>").Append(css.Replace("<", "\\3C ", StringComparison.Ordinal)).Append("</style>");
                return;
            }

            if (DropWithContent.TryGetValue(tag, out var cat))
            {
                // A plain <meta charset/name> is harmless noise; http-equiv (refresh, CSP override) is counted.
                if (tag != "meta" || attrs.Any(a => a.Key == "http-equiv")) Count(cat);
                if (raw is null && !selfClose && !Void.Contains(tag))
                {
                    _skipTag = tag;
                    _skipDepth = 1;
                }
                return;
            }

            if (Unwrap.Contains(tag))
            {
                CountHandlers(attrs); // <body onload>
                if (tag == "form") _forms++;
                return;
            }

            if (!Allowed.Contains(tag)) return; // unknown element: unwrap, keep its content

            if (tag == "img") { EmitImage(attrs); return; }

            if (Block.Contains(tag) || tag is "td" or "th") BreakText(tag);

            var sb = _out;
            sb.Append('<').Append(tag);
            bool hasId = false;
            string? href = null;
            foreach (var (name, value) in attrs)
            {
                if (!AllowAttr(tag, name, value, out var outValue)) continue;
                if (name == "href") { href = outValue; continue; }
                if (name == "id") hasId = true;
                sb.Append(' ').Append(name).Append("=\"").Append(Encode(outValue)).Append('"');
            }
            if (tag == "a" && href is not null)
            {
                sb.Append(" href=\"").Append(Encode(href)).Append('"');
                if (!href.StartsWith('#')) sb.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
            }

            bool isOutlineHeading = tag is "h1" or "h2" or "h3" && _headingText is null;
            string? headingId = null;
            if (isOutlineHeading)
            {
                headingId = attrs.Where(a => a.Key == "id").Select(a => a.Value.Trim()).FirstOrDefault(IsSafeId);
                if (!hasId || headingId is null)
                {
                    do headingId = "chvd-h-" + (++_genId).ToString(CultureInfo.InvariantCulture);
                    while (_ids.Contains(headingId));
                    if (!hasId) sb.Append(" id=\"").Append(headingId).Append('"');
                }
            }
            sb.Append('>');

            if (Void.Contains(tag)) return;
            _stack.Add(tag);
            if (tag == "pre") _pre++;
            if (isOutlineHeading)
            {
                _headingDepth = _stack.Count - 1;
                _headingId = headingId;
                _headingLevel = tag[1] - '0';
                _headingText = new StringBuilder();
            }
        }

        private void EndTag(string tag)
        {
            if (_skipDepth > 0)
            {
                if (tag == _skipTag && --_skipDepth == 0) _skipTag = null;
                return;
            }
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i] != tag) continue;
                while (_stack.Count > i) Close(_stack.Count - 1);
                return;
            }
            // stray end tag: ignored (keeps output well-formed)
        }

        private void Close(int index)
        {
            var tag = _stack[index];
            _stack.RemoveAt(index);
            _out.Append("</").Append(tag).Append('>');
            if (tag == "pre") _pre--;
            if (Block.Contains(tag)) BreakText(tag);
            if (_headingText is not null && index == _headingDepth)
            {
                var title = Collapse(_headingText.ToString()).Trim();
                if (title.Length > 0 && _headingId is not null)
                {
                    _outline.Add(new ViewerOutlineEntry(title, _headingLevel, _headingId));
                    _ids.Add(_headingId);
                }
                _headingText = null;
                _headingDepth = -1;
                _headingId = null;
            }
        }

        private bool AllowAttr(string tag, string name, string value, out string outValue)
        {
            outValue = value;
            if (name.StartsWith("on", StringComparison.Ordinal)) { _handlers++; return false; }
            if (name is "srcset" or "imagesrcset" or "sizes" or "ping" or "background" or "formaction"
                     or "action" or "poster" or "xlink:href" or "lowsrc" or "dynsrc" or "longdesc" or "cite")
            {
                if (name is "srcset" or "imagesrcset" or "ping" or "background" or "formaction" or "action"
                    or "poster" or "xlink:href" or "lowsrc" or "dynsrc")
                    _other++;
                return false;
            }
            if (name.Contains(':')) return false;
            if (name.StartsWith("aria-", StringComparison.Ordinal)) return !HasControlChars(value);

            bool allowed = GlobalAttrs.Contains(name)
                || (ElementAttrs.TryGetValue(tag, out var list) && Array.IndexOf(list, name) >= 0);
            if (!allowed) return false;

            switch (name)
            {
                case "style":
                    if (IsDangerousCss(value)) { _styles++; return false; }
                    return true;
                case "id":
                case "name":
                    outValue = value.Trim();
                    return IsSafeId(outValue);
                case "href":
                    var url = CleanUrl(value);
                    if (url.StartsWith('#')) { outValue = url; return url.Length > 1 && !HasControlChars(url); }
                    var scheme = SchemeOf(url);
                    if (scheme is "http" or "https" or "mailto" && !HasControlChars(url)) { outValue = url; return true; }
                    if (scheme is not null) _urls++; // javascript:, vbscript:, data:, file: … (relative links just drop)
                    return false;
                default:
                    return !HasControlChars(value);
            }
        }

        private void EmitImage(List<KeyValuePair<string, string>> attrs)
        {
            string? src = null, alt = null;
            foreach (var (n, v) in attrs)
            {
                if (n == "src") src = CleanUrl(v);
                else if (n == "alt") alt = v;
            }

            bool ok = false;
            bool remote = false;
            if (!string.IsNullOrEmpty(src))
            {
                var scheme = SchemeOf(src);
                if (scheme == "data")
                    ok = src.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && !HasControlChars(src);
                else if (scheme is "http" or "https" || src.StartsWith("//", StringComparison.Ordinal))
                {
                    remote = true;
                    ok = _remote && !HasControlChars(src);
                }
                if (scheme is "javascript" or "vbscript") _urls++;
            }

            if (!ok)
            {
                CountHandlers(attrs); // <img src=x onerror=…>
                if (remote) _remoteImgs++;
                else _other++;
                var label = string.IsNullOrWhiteSpace(alt) ? "image" : "image: " + Collapse(alt).Trim();
                _out.Append("<span class=\"").Append(ImagePlaceholderClass).Append("\" title=\"")
                    .Append(remote ? "Remote image blocked" : "Image not available").Append("\">[")
                    .Append(Encode(label)).Append("]</span>");
                return;
            }

            if (src!.StartsWith("//", StringComparison.Ordinal)) src = "https:" + src;
            _out.Append("<img src=\"").Append(Encode(src)).Append('"');
            foreach (var (n, v) in attrs)
            {
                if (n == "src") continue;
                if (AllowAttr("img", n, v, out var ov)) _out.Append(' ').Append(n).Append("=\"").Append(Encode(ov)).Append('"');
            }
            _out.Append(" referrerpolicy=\"no-referrer\">");
            if (!string.IsNullOrWhiteSpace(alt)) AppendPlain(" " + alt + " ");
        }

        private static string CleanUrl(string v) => v.Trim();

        private void CountHandlers(List<KeyValuePair<string, string>> attrs)
        {
            foreach (var a in attrs)
                if (a.Key.StartsWith("on", StringComparison.Ordinal)) _handlers++;
        }

        private static bool IsSafeId(string v)
        {
            if (v.Length == 0 || v.Length > 200) return false;
            foreach (var c in v)
                if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')) return false;
            return true;
        }

        private void Count(Category c)
        {
            switch (c)
            {
                case Category.Script: _scripts++; break;
                case Category.Embed: _embeds++; break;
                case Category.Form: _forms++; break;
                case Category.Other: _other++; break;
            }
        }

        // ── text ──────────────────────────────────────────────────────────────────────────────
        private void EmitText(string raw)
        {
            if (_skipDepth > 0 || raw.Length == 0) return;
            var text = WebUtility.HtmlDecode(raw);
            AppendPlain(text);
            _headingText?.Append(text);

            if (_hl is null) { _out.Append(Encode(text)); return; }
            int start = 0;
            while (true)
            {
                int k = text.IndexOf(_hl, start, StringComparison.OrdinalIgnoreCase);
                if (k < 0) break;
                _out.Append(Encode(text[start..k]));
                _out.Append("<mark class=\"").Append(HitClass).Append("\">")
                    .Append(Encode(text.Substring(k, _hl.Length))).Append("</mark>");
                _hits++;
                start = k + _hl.Length;
            }
            _out.Append(Encode(text[start..]));
        }

        private void AppendPlain(string text)
        {
            if (_pre > 0) { _text.Append(text); return; }
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (_text.Length > 0 && !char.IsWhiteSpace(_text[^1])) _text.Append(' ');
                }
                else _text.Append(c);
            }
        }

        private void BreakText(string tag)
        {
            // Trim the trailing space on the current line, then start a new one.
            while (_text.Length > 0 && _text[^1] == ' ') _text.Length--;
            if (tag is "td" or "th")
            {
                if (_text.Length > 0 && _text[^1] != '\n') _text.Append('\t');
                return;
            }
            if (_text.Length > 0 && _text[^1] != '\n') _text.Append('\n');
        }

        private static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string NormalizeText(string t)
        {
            var lines = t.Split('\n');
            var sb = new StringBuilder(t.Length);
            int blank = 0;
            foreach (var l in lines)
            {
                var line = l.TrimEnd();
                if (line.Trim().Length == 0) { if (++blank > 1) continue; }
                else blank = 0;
                sb.Append(line).Append('\n');
            }
            return sb.ToString().Trim('\n');
        }
    }
}
