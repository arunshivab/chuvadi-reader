using ChuvadiReader.Core.Reader;

namespace ChuvadiReader.Ui.Components.Cards;

/// <summary>Shared status → style/icon/label mapping for the signature card and banner.</summary>
internal static class SignatureStyles
{
    public static string Css(SignatureCheckStatus s) => s switch
    {
        SignatureCheckStatus.Valid => "ok",
        SignatureCheckStatus.UnknownTrust or SignatureCheckStatus.ModifiedAfterSigning => "warn",
        SignatureCheckStatus.Invalid => "bad",
        _ => "muted",
    };

    public static string IconFor(SignatureCheckStatus s) => s switch
    {
        SignatureCheckStatus.Valid => "certification",
        SignatureCheckStatus.Invalid => "warning",
        SignatureCheckStatus.ModifiedAfterSigning or SignatureCheckStatus.UnknownTrust => "warning",
        _ => "certification",
    };

    public static string Label(SignatureCheckStatus s) => s switch
    {
        SignatureCheckStatus.Valid => "Valid",
        SignatureCheckStatus.UnknownTrust => "Signer not verified",
        SignatureCheckStatus.ModifiedAfterSigning => "Changed after signing",
        SignatureCheckStatus.Invalid => "Not valid",
        _ => "Can't be checked",
    };
}
