namespace ChuvadiReader.Core.Theme;

/// <summary>The five manuscript themes. The rendered PDF page stays white in every theme.</summary>
public enum ChuvadiTheme
{
    /// <summary>Cream paper (default).</summary>
    Light,

    /// <summary>Night paper.</summary>
    Dark,

    /// <summary>Palm leaf.</summary>
    Palm,

    /// <summary>Indigo ink.</summary>
    Indigo,

    /// <summary>High contrast — kiosk and low vision.</summary>
    Contrast,
}

public static class ChuvadiThemeExtensions
{
    /// <summary>The <c>data-theme</c> attribute value used by tokens.css.</summary>
    public static string Key(this ChuvadiTheme theme) => theme switch
    {
        ChuvadiTheme.Dark => "dark",
        ChuvadiTheme.Palm => "palm",
        ChuvadiTheme.Indigo => "indigo",
        ChuvadiTheme.Contrast => "contrast",
        _ => "light",
    };

    /// <summary>The name shown to people (Settings › Appearance, Ctrl K).</summary>
    public static string DisplayName(this ChuvadiTheme theme) => theme switch
    {
        ChuvadiTheme.Dark => "Night paper",
        ChuvadiTheme.Palm => "Palm leaf",
        ChuvadiTheme.Indigo => "Indigo ink",
        ChuvadiTheme.Contrast => "High contrast",
        _ => "Cream paper",
    };

    /// <summary>True for the one dark theme (used by the host for its pre-load background).</summary>
    public static bool IsDark(this ChuvadiTheme theme) => theme == ChuvadiTheme.Dark;
}
