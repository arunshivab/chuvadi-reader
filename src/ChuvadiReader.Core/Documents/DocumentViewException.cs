namespace ChuvadiReader.Core.Documents;

public enum ViewErrorReason { PasswordProtected, BadFormat, Other }

/// <summary>Raised by the Docs/Sheets view-services so the Ui can react without
/// referencing the underlying document libraries.</summary>
public sealed class DocumentViewException : Exception
{
    public ViewErrorReason Reason { get; }

    public DocumentViewException(ViewErrorReason reason, string message, Exception? inner = null)
        : base(message, inner) => Reason = reason;
}
