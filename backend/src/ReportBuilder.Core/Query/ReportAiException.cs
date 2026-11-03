namespace ReportBuilder.Core.Query;

public sealed class ReportAiException : Exception
{
    public int StatusCode { get; }

    public ReportAiException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
