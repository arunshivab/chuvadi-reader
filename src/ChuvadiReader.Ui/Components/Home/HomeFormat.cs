namespace ChuvadiReader.Ui.Components.Home;

/// <summary>Small display formatters shared by Home and Settings.</summary>
public static class HomeFormat
{
    /// <summary>"4m", "3h", "yesterday", "6d" — or "never".</summary>
    public static string Relative(DateTimeOffset? when)
    {
        if (when is null)
        {
            return "never";
        }

        var d = DateTimeOffset.Now - when.Value;
        if (d.TotalMinutes < 60)
        {
            return $"{Math.Max(1, (int)d.TotalMinutes)}m";
        }

        if (d.TotalHours < 24)
        {
            return $"{(int)d.TotalHours}h";
        }

        return d.TotalDays < 2 ? "yesterday" : $"{(int)d.TotalDays}d";
    }

    /// <summary>"512 B", "1.4 MB"…</summary>
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double s = bytes;
        var i = 0;
        while (s >= 1024 && i < units.Length - 1)
        {
            s /= 1024;
            i++;
        }

        return $"{s:0.#} {units[i]}";
    }
}
