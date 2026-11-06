namespace ChuvadiReader.Ui.Components.Platform.Phone;

/// <summary>Which bottom sheet the phone shell shows.</summary>
public enum PhoneSheetKind
{
    None,

    /// <summary>The ⋮ menu: Open · Find · Library · Settings · View, then the Document menu.</summary>
    Menu,

    /// <summary>The View sheet (12j).</summary>
    View,
}
