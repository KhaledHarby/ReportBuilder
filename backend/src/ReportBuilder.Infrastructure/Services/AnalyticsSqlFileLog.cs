using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using ReportBuilder.Core.Query;

namespace ReportBuilder.Infrastructure.Services;

public interface IAnalyticsSqlLog
{
    IAnalyticsSqlLogSession Begin(Guid dashboardId, string dashboardName, string kind);
}

public interface IAnalyticsSqlLogSession : IAsyncDisposable
{
    void Record(string name, AnalyticsQueryBuilder.BuiltAnalyticsQuery query);
}

/// <summary>Used when SQL logging is disabled so a request does no logging work at all.</summary>
public sealed class NullAnalyticsSqlLog : IAnalyticsSqlLog
{
    private static readonly Session Shared = new();

    public IAnalyticsSqlLogSession Begin(Guid dashboardId, string dashboardName, string kind) => Shared;

    private sealed class Session : IAnalyticsSqlLogSession
    {
        public void Record(string name, AnalyticsQueryBuilder.BuiltAnalyticsQuery query)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed record AnalyticsSqlLogFile(
    string Kind,
    Guid DashboardId,
    string DashboardName,
    DateTimeOffset Started,
    string Content);

/// <summary>
/// Writes the generated analytics SQL to disk for troubleshooting. Files are handed to a background
/// writer rather than written inline, so logging never adds disk I/O to a dashboard request, and the
/// queue is bounded so a burst drops entries instead of growing memory.
/// </summary>
public sealed class AnalyticsSqlFileLog : IAnalyticsSqlLog
{
    private readonly Channel<AnalyticsSqlLogFile> _queue;

    public AnalyticsSqlFileLog(Channel<AnalyticsSqlLogFile> queue)
    {
        _queue = queue;
    }

    public IAnalyticsSqlLogSession Begin(Guid dashboardId, string dashboardName, string kind)
        => new Session(_queue, dashboardId, dashboardName, kind);

    private sealed class Session : IAnalyticsSqlLogSession
    {
        private readonly Channel<AnalyticsSqlLogFile> _queue;
        private readonly Guid _dashboardId;
        private readonly string _dashboardName;
        private readonly string _kind;
        private readonly ConcurrentQueue<Entry> _entries = new();
        private readonly DateTimeOffset _started = DateTimeOffset.Now;

        public Session(Channel<AnalyticsSqlLogFile> queue, Guid dashboardId, string dashboardName, string kind)
        {
            _queue = queue;
            _dashboardId = dashboardId;
            _dashboardName = dashboardName;
            _kind = string.IsNullOrWhiteSpace(kind) ? "query" : kind.Trim();
        }

        public void Record(string name, AnalyticsQueryBuilder.BuiltAnalyticsQuery query)
        {
            _entries.Enqueue(new Entry(name, query.Sql, query.Parameters));
        }

        public ValueTask DisposeAsync()
        {
            if (_entries.IsEmpty || _kind is not ("snapshot" or "data"))
            {
                return ValueTask.CompletedTask;
            }

            _queue.Writer.TryWrite(new AnalyticsSqlLogFile(
                _kind,
                _dashboardId,
                _dashboardName,
                _started,
                Render()));
            return ValueTask.CompletedTask;
        }

        private string Render()
        {
            var label = _kind == "data" ? "sql" : "query";
            var text = new StringBuilder();
            text.AppendLine($"-- analytics {label}")
                .AppendLine($"-- dashboard {_dashboardName}")
                .AppendLine($"-- id {_dashboardId:D}")
                .AppendLine($"-- started {_started:yyyy-MM-dd HH:mm:ss.fff zzz}")
                .AppendLine($"-- statements {_entries.Count}")
                .AppendLine();

            var index = 1;
            foreach (var entry in _entries)
            {
                text.AppendLine($"-- {index}. {entry.Name}")
                    .AppendLine("GO");
                foreach (var parameter in entry.Parameters)
                {
                    text.AppendLine($"-- {parameter.Name} {parameter.SqlType ?? "inferred"} = {FormatValue(parameter.Value)}");
                }

                text.AppendLine(entry.Sql.Trim())
                    .AppendLine()
                    .AppendLine("GO")
                    .AppendLine();
                index++;
            }

            return text.ToString();
        }

        private static string FormatValue(object? value)
        {
            if (value is null or DBNull)
            {
                return "NULL";
            }

            if (value is string text)
            {
                if (text.Length > 2000)
                {
                    text = text[..2000] + "…";
                }

                return "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "NULL";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
        }

        private sealed record Entry(string Name, string Sql, IReadOnlyList<QueryParameter> Parameters);
    }
}

public static class AnalyticsSqlLogOptions
{
    /// <summary>
    /// Off unless explicitly enabled. The files record full parameter values, so this is a
    /// troubleshooting aid to switch on deliberately rather than something that runs in production.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration)
        => configuration.GetValue("Analytics:StoreGeneratedQueries", false)
           || configuration.GetValue("Analytics:SqlLogEnabled", false);

    public static string ResolveDirectory(IConfiguration configuration)
    {
        var configured = configuration["Analytics:SqlLogDirectory"]?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "logs", "sql")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured);
    }
}
