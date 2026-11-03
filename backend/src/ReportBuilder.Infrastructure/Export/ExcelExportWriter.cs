using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Export;

internal static class ExcelExportWriter
{
    private const int ExcelSheetRowLimit = 1_048_576;

    public static void Write(ReportExportTable table, Stream output)
    {
        using var buffer = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = BuildStyles();
            styles.Stylesheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var rightToLeft = IsArabic(table);
            if (table.Columns.Count == 0)
            {
                var data = new SheetData();
                data.Append(TextRow(1, [table.Arabic ? "لا توجد أعمدة في هذا التقرير." : "This report has no columns."], header: false));
                sheets.Append(AddSheet(workbookPart, SheetName(table.Title, 1), data, rightToLeft));
            }
            else
            {
                var headerRows = ExportHeaderBands.Any(ExportHeaderBands.Aligned(table)) ? 2 : 1;
                var maxDataRows = ExcelSheetRowLimit - headerRows;
                var part = 0;
                for (var offset = 0; part == 0 || offset < table.Rows.Count; offset += maxDataRows)
                {
                    part++;
                    var count = table.Rows.Count == 0
                        ? 0
                        : Math.Min(maxDataRows, table.Rows.Count - offset);
                    sheets.Append(WriteDataSheet(
                        workbookPart,
                        SheetName(table.Title, part),
                        table,
                        offset,
                        count,
                        rightToLeft));
                    if (table.Rows.Count == 0)
                    {
                        break;
                    }
                }
            }

            document.PackageProperties.Title = ReportExportWriter.Sanitize(WorkbookTitle(table));
            workbookPart.Workbook.Save();
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static string WorkbookTitle(ReportExportTable table)
    {
        var english = table.Title.Trim();
        var arabic = table.TitleArabic?.Trim();
        if (string.IsNullOrWhiteSpace(arabic) || arabic.Equals(english, StringComparison.Ordinal))
        {
            return english;
        }

        return string.IsNullOrWhiteSpace(english) ? arabic : $"{english} - {arabic}";
    }

    private static Sheet WriteDataSheet(
        WorkbookPart workbookPart,
        string name,
        ReportExportTable table,
        int offset,
        int count,
        bool rightToLeft)
    {
        var styles = ExportHeaderBands.Aligned(table);
        var hasBands = ExportHeaderBands.Any(styles);
        var headerRows = hasBands ? 2 : 1;
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            var view = new SheetView { WorkbookViewId = 0U, RightToLeft = rightToLeft };
            if (count > 0)
            {
                view.Append(new Pane
                {
                    VerticalSplit = headerRows,
                    TopLeftCell = "A" + (headerRows + 1),
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                });
            }

            writer.WriteElement(new SheetViews(view));
            writer.WriteStartElement(new SheetData());
            if (hasBands)
            {
                writer.WriteElement(BandRow(styles, table.Columns));
                writer.WriteElement(ColumnHeaderRow(2, table.Columns, styles, skipUnbanded: true));
            }
            else
            {
                writer.WriteElement(ColumnHeaderRow(1, table.Columns, styles, skipUnbanded: false));
            }

            var bodyStyles = styles.Select(style => StyleIndex(style.Align, style.Wrap, header: false)).ToArray();
            for (var i = 0; i < count; i++)
            {
                var source = table.Rows[offset + i];
                var cells = new string[table.Columns.Count];
                for (var c = 0; c < cells.Length; c++)
                {
                    cells[c] = c < source.Count ? source[c] : string.Empty;
                }

                writer.WriteElement(TextRow((uint)(headerRows + 1 + i), cells, bodyStyles));
            }

            writer.WriteEndElement();
            var merges = MergeRanges(styles);
            if (merges.Count > 0)
            {
                writer.WriteStartElement(new MergeCells());
                foreach (var range in merges)
                {
                    writer.WriteElement(new MergeCell { Reference = range });
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        return new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = (uint)workbookPart.Workbook.Sheets!.Count() + 1,
            Name = name
        };
    }

    private static Sheet AddSheet(WorkbookPart workbookPart, string name, SheetData data, bool rightToLeft)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var view = new SheetView { WorkbookViewId = 0U, RightToLeft = rightToLeft };
        if (data.Elements<Row>().Skip(1).Any())
        {
            view.Append(new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });
        }

        worksheetPart.Worksheet = new Worksheet(new SheetViews(view), data);
        worksheetPart.Worksheet.Save();
        return new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = (uint)workbookPart.Workbook.Sheets!.Count() + 1,
            Name = name
        };
    }

    private static Row TextRow(uint index, IReadOnlyList<string> values, bool header)
        => TextRow(index, values, null, header ? StyleIndex(null, wrap: false, header: true) : 0U);

    private static Row BandRow(IReadOnlyList<ExportColumnStyle> styles, IReadOnlyList<string> columns)
    {
        var values = new string[columns.Count];
        var styleIndexes = new uint[columns.Count];
        foreach (var span in ExportHeaderBands.Spans(styles))
        {
            if (span.Title is null)
            {
                values[span.Start] = columns[span.Start];
                styleIndexes[span.Start] = StyleIndex(styles[span.Start].Align, styles[span.Start].Wrap, header: true);
            }
            else
            {
                values[span.Start] = span.Title;
                var bandStyle = StyleIndex("center", wrap: true, header: true);
                for (var i = 0; i < span.Count; i++)
                {
                    styleIndexes[span.Start + i] = bandStyle;
                }
            }
        }

        return TextRow(1, values, styleIndexes);
    }

    private static Row ColumnHeaderRow(
        uint index,
        IReadOnlyList<string> columns,
        IReadOnlyList<ExportColumnStyle> styles,
        bool skipUnbanded)
    {
        var values = new string[columns.Count];
        var styleIndexes = new uint[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var style = styles[i];
            var unbanded = string.IsNullOrEmpty(style.HeaderBand);
            values[i] = skipUnbanded && unbanded ? string.Empty : columns[i];
            styleIndexes[i] = StyleIndex(style.Align, style.Wrap, header: true);
        }

        return TextRow(index, values, styleIndexes);
    }

    private static List<string> MergeRanges(IReadOnlyList<ExportColumnStyle> styles)
    {
        if (!ExportHeaderBands.Any(styles))
        {
            return [];
        }

        var ranges = new List<string>();
        foreach (var span in ExportHeaderBands.Spans(styles))
        {
            var start = ColumnLetter(span.Start);
            if (span.Title is null)
            {
                ranges.Add($"{start}1:{start}2");
            }
            else if (span.Count > 1)
            {
                ranges.Add($"{start}1:{ColumnLetter(span.Start + span.Count - 1)}1");
            }
        }

        return ranges;
    }

    private static uint StyleIndex(string? align, bool wrap, bool header)
    {
        var alignIndex = align switch
        {
            "center" => 1,
            "right" => 2,
            _ => 0
        };
        return (uint)(1 + (header ? 6 : 0) + (wrap ? 3 : 0) + alignIndex);
    }

    private static Row TextRow(uint index, IReadOnlyList<string> values, IReadOnlyList<uint>? styles, uint fallback = 0)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < values.Count; i++)
        {
            var text = ReportExportWriter.Sanitize(values[i]);
            if (text.Length > 32_767)
            {
                text = text[..32_767];
            }

            row.Append(new Cell
            {
                CellReference = ColumnLetter(i) + index,
                DataType = CellValues.InlineString,
                StyleIndex = styles is null ? fallback : styles[i],
                InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve })
            });
        }

        return row;
    }

    private static Stylesheet BuildStyles()
    {
        var fonts = new Fonts(
            new Font(),
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }))
        { Count = 2U };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(
                new ForegroundColor { Rgb = "FFB5511F" })
            { PatternType = PatternValues.Solid }))
        { Count = 3U };
        var borders = new Borders(new Border()) { Count = 1U };
        var formats = new CellFormats { Count = 13U };
        formats.Append(new CellFormat());
        for (var header = 0; header < 2; header++)
        {
            for (var wrap = 0; wrap < 2; wrap++)
            {
                foreach (var horizontal in new[] { HorizontalAlignmentValues.Left, HorizontalAlignmentValues.Center, HorizontalAlignmentValues.Right })
                {
                    var format = new CellFormat
                    {
                        Alignment = new Alignment
                        {
                            Horizontal = horizontal,
                            Vertical = wrap == 1 ? VerticalAlignmentValues.Top : VerticalAlignmentValues.Center,
                            WrapText = wrap == 1
                        },
                        ApplyAlignment = true
                    };
                    if (header == 1)
                    {
                        format.FontId = 1U;
                        format.FillId = 2U;
                        format.ApplyFont = true;
                        format.ApplyFill = true;
                    }

                    formats.Append(format);
                }
            }
        }

        return new Stylesheet(fonts, fills, borders, formats);
    }

    private static string SheetName(string? title, int part)
    {
        var cleaned = ReportExportWriter.Sanitize(title);
        var builder = new System.Text.StringBuilder(Math.Min(cleaned.Length, 31));
        foreach (var ch in cleaned)
        {
            if (ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']')
            {
                continue;
            }

            builder.Append(ch);
            if (builder.Length == 31)
            {
                break;
            }
        }

        var name = builder.ToString().Trim();
        if (name.Length == 0)
        {
            name = "Report";
        }

        if (part <= 1)
        {
            return name;
        }

        var suffix = $" ({part})";
        var room = 31 - suffix.Length;
        var stem = name.Length <= room ? name : name[..room].Trim();
        if (stem.Length == 0)
        {
            stem = "Report";
        }

        return stem + suffix;
    }

    private static string ColumnLetter(int index)
    {
        var number = index + 1;
        var letters = string.Empty;
        while (number > 0)
        {
            var remainder = (number - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            number = (number - 1) / 26;
        }

        return letters;
    }

    private static bool IsArabic(ReportExportTable table)
        => table.Arabic
            || PdfArabicText.ContainsArabic(table.Title)
            || table.Columns.Any(PdfArabicText.ContainsArabic);
}
