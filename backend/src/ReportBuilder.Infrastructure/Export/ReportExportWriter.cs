using System.Globalization;
using System.Text;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Export;

internal static class ReportExportWriter
{
    public static int? RowCapFor(ReportExportFormat format)
        => ReportExportLimits.RowCapFor(format);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static void Write(ReportExportTable table, ReportExportFormat format, Stream output)
    {
        switch (format)
        {
            case ReportExportFormat.Pdf:
                PdfExportWriter.Write(table, output);
                break;
            case ReportExportFormat.Docx:
                WordExportWriter.Write(table, output);
                break;
            case ReportExportFormat.Xlsx:
                ExcelExportWriter.Write(table, output);
                break;
            default:
                WriteCsvAsync(table, output, CancellationToken.None).GetAwaiter().GetResult();
                break;
        }
    }

    public static Task WriteAsync(
        ReportExportTable table,
        ReportExportFormat format,
        Stream output,
        CancellationToken cancellationToken)
    {
        if (format == ReportExportFormat.Csv)
        {
            return WriteCsvAsync(table, output, cancellationToken);
        }

        Write(table, format, output);
        return Task.CompletedTask;
    }

    private static async Task WriteCsvAsync(ReportExportTable table, Stream output, CancellationToken cancellationToken)
    {
        await WriteUtf8BomAsync(output, cancellationToken);
        await using var writer = CreateCsvWriter(output);
        await WriteCsvRecordAsync(writer, table.Columns, cancellationToken);
        foreach (var row in table.Rows)
        {
            await WriteCsvRecordAsync(writer, row, cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    internal static Task WriteUtf8BomAsync(Stream output, CancellationToken cancellationToken)
        => output.WriteAsync(Utf8Bom, cancellationToken).AsTask();

    internal static StreamWriter CreateCsvWriter(Stream output)
        => new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 65_536, leaveOpen: true);

    internal static Task WriteCsvRecordAsync(StreamWriter writer, IReadOnlyList<string> cells, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return writer.WriteLineAsync(string.Join(",", cells.Select(CsvEscape)));
    }

    internal static string CsvEscape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string FormatValue(object? value)
        => value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsControl(ch) || ch is '\t')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
