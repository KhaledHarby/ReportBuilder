namespace ReportBuilder.Core.Identity;

public sealed class IdentityException : Exception
{
    public int StatusCode { get; }

    public IdentityException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
