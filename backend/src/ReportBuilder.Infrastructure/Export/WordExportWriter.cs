using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Export;

internal static class WordExportWriter
{
    public static void Write(ReportExportTable table, Stream output)
    {
        using var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);

        body.AppendChild(TitleParagraph(table.Title, right: false));
        var arabicTitle = table.TitleArabic?.Trim();
        if (!string.IsNullOrWhiteSpace(arabicTitle)
            && !arabicTitle.Equals(table.Title.Trim(), StringComparison.Ordinal))
        {
            body.AppendChild(TitleParagraph(arabicTitle, right: true));
        }

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "200" }),
            new Run(
                new RunProperties(
                    new FontSize { Val = "18" },
                    new Color { Val = "6D6478" }),
                new Text(ReportExportWriter.Sanitize(BuildMetaLine(table)))
                {
                    Space = SpaceProcessingModeValues.Preserve
                })));

        if (table.Columns.Count > 0)
        {
            var wordTable = new Table();
            wordTable.AppendChild(new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "D8CEC0" },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "D8CEC0" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D8CEC0" },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = "D8CEC0" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E7DFD4" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E7DFD4" })));

            var styles = ExportHeaderBands.Aligned(table);
            var hasBands = ExportHeaderBands.Any(styles);
            if (hasBands)
            {
                var bandRow = new TableRow();
                bandRow.AppendChild(new TableRowProperties(new TableHeader()));
                foreach (var span in ExportHeaderBands.Spans(styles))
                {
                    if (span.Title is null)
                    {
                        var style = styles[span.Start];
                        bandRow.AppendChild(CreateCell(
                            table.Columns[span.Start],
                            header: true,
                            style.Align,
                            style.Wrap,
                            mergeRestart: true));
                    }
                    else
                    {
                        bandRow.AppendChild(CreateCell(
                            span.Title,
                            header: true,
                            "center",
                            wrap: true,
                            gridSpan: span.Count));
                    }
                }

                wordTable.AppendChild(bandRow);
            }

            var header = new TableRow();
            header.AppendChild(new TableRowProperties(new TableHeader()));
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var style = styles[i];
                var unbanded = hasBands && string.IsNullOrEmpty(style.HeaderBand);
                header.AppendChild(CreateCell(
                    unbanded ? string.Empty : table.Columns[i],
                    header: true,
                    style.Align,
                    style.Wrap,
                    mergeContinue: unbanded));
            }

            wordTable.AppendChild(header);

            foreach (var row in table.Rows)
            {
                var wordRow = new TableRow();
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var text = i < row.Count ? row[i] : string.Empty;
                    wordRow.AppendChild(CreateCell(text, header: false, styles[i].Align, styles[i].Wrap));
                }

                wordTable.AppendChild(wordRow);
            }

            body.AppendChild(wordTable);
        }

        if (table.Truncated)
        {
            body.AppendChild(new Paragraph(
                new Run(
                    new RunProperties(new Color { Val = "6D6478" }, new FontSize { Val = "18" }),
                    new Text("Showing the first 20,000 rows. Export CSV for the full result.")
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    })));
        }

        var landscape = table.Columns.Count > 6;
        body.AppendChild(new SectionProperties(
            new PageSize
            {
                Width = landscape ? 16838U : 11906U,
                Height = landscape ? 11906U : 16838U,
                Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait
            },
            new PageMargin
            {
                Top = 720,
                Right = 720U,
                Bottom = 720,
                Left = 720U
            }));

            main.Document.Save();
            document.Save();
        }

        if (buffer.CanSeek)
        {
            buffer.Position = 0;
        }

        buffer.CopyTo(output);
    }

    private static Paragraph TitleParagraph(string text, bool right)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines { After = "80" });
        if (right)
        {
            properties.AppendChild(new Justification { Val = JustificationValues.Right });
            properties.AppendChild(new BiDi());
        }

        var runProps = new RunProperties(
            new Bold(),
            new FontSize { Val = "32" },
            new Color { Val = "211B2A" },
            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Arial" });
        if (right)
        {
            runProps.AppendChild(new RightToLeftText());
        }

        return new Paragraph(
            properties,
            new Run(
                runProps,
                new Text(ReportExportWriter.Sanitize(text)) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static TableCell CreateCell(
        string text,
        bool header,
        string? align = null,
        bool wrap = false,
        int gridSpan = 1,
        bool mergeRestart = false,
        bool mergeContinue = false)
    {
        var runProps = new RunProperties(
            new FontSize { Val = header ? "18" : "16" },
            new Color { Val = header ? "FFFDF8" : "211B2A" },
            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Arial" });
        if (header)
        {
            runProps.AppendChild(new Bold());
        }

        var rtl = PdfArabicText.ContainsArabic(text) && align != "left";
        if (rtl)
        {
            runProps.AppendChild(new RightToLeftText());
        }

        var paragraphProps = new ParagraphProperties(new SpacingBetweenLines { After = "0", Before = "0" });
        JustificationValues? justification = align switch
        {
            "left" => JustificationValues.Left,
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => rtl ? JustificationValues.Right : null
        };
        if (justification is { } value)
        {
            paragraphProps.AppendChild(new Justification { Val = value });
        }

        if (rtl)
        {
            paragraphProps.AppendChild(new BiDi());
        }

        var paragraph = new Paragraph(
            paragraphProps,
            new Run(runProps, new Text(ReportExportWriter.Sanitize(text)) { Space = SpaceProcessingModeValues.Preserve }));

        var cellProps = new TableCellProperties(
            new TableCellVerticalAlignment
            {
                Val = wrap ? TableVerticalAlignmentValues.Top : TableVerticalAlignmentValues.Center
            },
            new TableCellMargin(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "80", Type = TableWidthUnitValues.Dxa }));
        if (gridSpan > 1)
        {
            cellProps.AppendChild(new GridSpan { Val = gridSpan });
        }

        if (mergeRestart)
        {
            cellProps.AppendChild(new VerticalMerge { Val = MergedCellValues.Restart });
        }
        else if (mergeContinue)
        {
            cellProps.AppendChild(new VerticalMerge());
        }

        if (!wrap)
        {
            cellProps.AppendChild(new NoWrap());
        }

        if (header)
        {
            cellProps.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Fill = "B5511F",
                Color = "auto"
            });
        }

        return new TableCell(cellProps, paragraph);
    }

    private static string BuildMetaLine(ReportExportTable table)
    {
        var line = $"Generated {DateTime.Now:yyyy-MM-dd HH:mm} · {table.Rows.Count} rows";
        if (!string.IsNullOrWhiteSpace(table.ExportedBy))
        {
            line += $" · Exported by {table.ExportedBy.Trim()}";
        }

        return line;
    }
}
