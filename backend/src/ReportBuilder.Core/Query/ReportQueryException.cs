namespace ReportBuilder.Core.Query;

public sealed class ReportQueryException : Exception
{
    public ReportQueryException(string message) : base(message)
    {
    }
}
