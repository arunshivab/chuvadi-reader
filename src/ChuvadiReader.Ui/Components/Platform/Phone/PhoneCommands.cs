using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Ui.Components.Platform.Phone;

/// <summary>One item of the phone tool strip (Portal &amp; Android 12h / 12j).</summary>
/// <param name="Label">Short label under the icon, as in the design.</param>
/// <param name="Icon">LiPicons key, as in the design.</param>
/// <param name="CommandIds">Shell command ids that carry the action, first registered wins
/// (the Reader and the redaction view register different ids for the same tool).</param>
public sealed record StripItem(string Label, string Icon, params string[] CommandIds);

/// <summary>One row of the phone View sheet (12j).</summary>
public sealed record ViewRow(string Label, string Icon, params string[] CommandIds);

/// <summary>
/// The phone chrome runs the actions the mode views already publish to <see cref="ShellService"/>
/// for the Ctrl K command bar, by id. Nothing is re-implemented here: a strip item whose command
/// is not registered is not shown, and a registered command that is disabled shows greyed with its
/// reason. The ids are a contract with Reader.Shell.cs, Redact.Shell.cs and Bench.Shell.cs; the
/// mobile-ui tests fail if one of them disappears.
/// </summary>
public static class PhoneCommands
{
    /// <summary>12h: Text · Mark · Note · Image · Box · Draw · Sign.</summary>
    public static readonly IReadOnlyList<StripItem> Mark = new StripItem[]
    {
        new("Text", "text-box", "m.text"),
        new("Mark", "highlight", "m.highlight"),
        new("Note", "sticky-note", "m.note"),
        new("Image", "file-image", "m.image"),
        new("Box", "shape-rect", "m.rect"),
        new("Draw", "freehand", "m.freehand"),
        new("Sign", "signature", "m.sign"),
    };

    /// <summary>12j: Box · Find · Stamp · Flatten · Lock · Sign. In the redaction view the
    /// redaction tools are its own commands (rd.*); in the Reader they open that view (p.*).</summary>
    public static readonly IReadOnlyList<StripItem> Protect = new StripItem[]
    {
        new("Box", "redact", "rd.box", "p.redact"),
        new("Find", "find-in-page", "rd.find", "p.find"),
        new("Stamp", "stamp", "p.stamp"),
        new("Flatten", "compress", "doc.flatten"),
        new("Lock", "doc-lock", "doc.password"),
        new("Sign", "certification", "p.cert"),
    };

    /// <summary>12j View sheet: Fit width · Fit page · Two-page · Rotate view · Loupe.</summary>
    public static readonly IReadOnlyList<ViewRow> View = new ViewRow[]
    {
        new("Fit width", "fit-width", "v.fitw"),
        new("Fit page", "fit-page", "v.fitp"),
        new("Two-page", "page-facing", "v.two"),
        new("Rotate view", "rotate-cw", "v.rotr"),
        new("Loupe", "loupe", "v.loupe"),
    };

    /// <summary>The floating primary action: Mark → Save as new PDF (12h); Arrange → Add desk (12i).</summary>
    public const string MarkPrimary = "m.saveas";
    public const string ArrangePrimary = "a.desk";

    /// <summary>12i: "Bind all" sits where the page counter is.</summary>
    public const string ArrangeBind = "a.bind";

    /// <summary>Every id the phone chrome uses (for the contract test).</summary>
    public static IEnumerable<string> AllIds =>
        Mark.SelectMany(i => i.CommandIds)
            .Concat(Protect.SelectMany(i => i.CommandIds))
            .Concat(View.SelectMany(r => r.CommandIds))
            .Append(MarkPrimary).Append(ArrangePrimary).Append(ArrangeBind)
            .Distinct();

    /// <summary>First registered command among <paramref name="ids"/>, or null.</summary>
    public static ShellCommand? Find(IReadOnlyList<ShellCommand> commands, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            foreach (var c in commands)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }
        }

        return null;
    }
}
