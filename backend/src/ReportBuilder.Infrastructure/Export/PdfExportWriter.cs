using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Export;

internal static class PdfExportWriter
{
    static PdfExportWriter()
    {
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
    }

    public static void Write(ReportExportTable table, Stream output)
    {
        var document = new Document();
        document.Info.Title = DocumentTitle(table);
        document.Styles["Normal"]!.Font.Name = "Tahoma";
        document.Styles["Normal"]!.Font.Size = 8;

        var section = document.AddSection();
        var manyColumns = table.Columns.Count > 6;
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = manyColumns ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = "1cm";
        section.PageSetup.RightMargin = "1cm";
        var contentWidth = manyColumns ? Unit.FromMillimeter(277) : Unit.FromMillimeter(190);
        var letterhead = HasLetterhead(table);
        section.PageSetup.TopMargin = letterhead ? "3.6cm" : "1.2cm";
        section.PageSetup.BottomMargin = "1.6cm";
        section.PageSetup.HeaderDistance = "0.45cm";
        section.PageSetup.FooterDistance = "0.45cm";
        string? logoPath = null;
        if (letterhead)
        {
            logoPath = AddLetterhead(section, table, contentWidth);
        }

        AddReportTitle(section, table, contentWidth);

        var metaLine = BuildMetaLine(table);
        var meta = section.AddParagraph(PdfArabicText.Prepare(metaLine));
        meta.Format.Font.Size = 8;
        meta.Format.Font.Color = new Color(0xFF6D6478);
        meta.Format.SpaceAfter = "4mm";
        if (PdfArabicText.ContainsArabic(metaLine))
        {
            meta.Format.Alignment = ParagraphAlignment.Right;
        }

        if (table.Columns.Count == 0)
        {
            section.AddParagraph(table.Arabic ? "لا توجد أعمدة في هذا التقرير." : "This report has no columns.");
        }
        else
        {
            var pdfTable = section.AddTable();
            pdfTable.Borders.Width = 0.25;
            pdfTable.Borders.Color = new Color(0xFFD8CEC0);
            pdfTable.Rows.LeftIndent = 0;

            foreach (var width in FitColumnWidths(table, contentWidth))
            {
                var column = pdfTable.AddColumn(width);
                column.LeftPadding = Unit.FromMillimeter(1.1);
                column.RightPadding = Unit.FromMillimeter(1.1);
            }

            var styles = ExportHeaderBands.Aligned(table);
            var hasBands = ExportHeaderBands.Any(styles);
            if (hasBands)
            {
                var bandRow = pdfTable.AddRow();
                StyleHeaderRow(bandRow);
                foreach (var span in ExportHeaderBands.Spans(styles))
                {
                    var cell = bandRow.Cells[span.Start];
                    if (span.Title is null)
                    {
                        cell.MergeDown = 1;
                        FillCell(cell, table.Columns[span.Start], styles[span.Start].Align, styles[span.Start].Wrap);
                    }
                    else
                    {
                        if (span.Count > 1)
                        {
                            cell.MergeRight = span.Count - 1;
                        }

                        FillCell(cell, span.Title, "center", wrap: true);
                    }
                }
            }

            var header = pdfTable.AddRow();
            StyleHeaderRow(header);
            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (hasBands && string.IsNullOrEmpty(styles[i].HeaderBand))
                {
                    continue;
                }

                FillCell(header.Cells[i], table.Columns[i], styles[i].Align, styles[i].Wrap);
            }

            var stripe = new Color(0xFFFBF8F4);
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = pdfTable.AddRow();
                if (r % 2 == 1)
                {
                    row.Shading.Color = stripe;
                }

                var cells = table.Rows[r];
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var raw = i < cells.Count ? cells[i] : string.Empty;
                    FillCell(row.Cells[i], raw, styles[i].Align, styles[i].Wrap);
                }
            }
        }

        if (table.Truncated)
        {
            var note = section.AddParagraph(table.Arabic
                ? "يُعرض أول 20,000 صف. نزّل CSV للحصول على النتيجة كاملة."
                : "Showing the first 20,000 rows. Export CSV for the full result.");
            note.Format.Font.Size = 8;
            note.Format.Font.Color = new Color(0xFF6D6478);
            note.Format.SpaceBefore = "4mm";
        }

        AddFooter(section, table, contentWidth);

        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };
        try
        {
            renderer.RenderDocument();
            renderer.Save(output, closeStream: false);
        }
        finally
        {
            if (logoPath is not null)
            {
                try
                {
                    File.Delete(logoPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static Unit[] FitColumnWidths(ReportExportTable table, Unit contentWidth)
    {
        var count = table.Columns.Count;
        var available = Math.Max(count * 16d, contentWidth.Point - 4);
        double[] preferred;
        try
        {
            preferred = MeasurePreferredWidths(table, available);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            preferred = Enumerable.Repeat(available / count, count).ToArray();
        }

        var sum = preferred.Sum();
        if (sum <= 0)
        {
            preferred = Enumerable.Repeat(1d, count).ToArray();
            sum = count;
        }

        var widths = new double[count];
        var scale = available / sum;
        for (var i = 0; i < count; i++)
        {
            widths[i] = preferred[i] * scale;
        }

        const double minPt = 32;
        if (minPt * count < available)
        {
            var deficit = 0d;
            var flexible = 0d;
            for (var i = 0; i < count; i++)
            {
                if (widths[i] < minPt)
                {
                    deficit += minPt - widths[i];
                    widths[i] = minPt;
                }
                else
                {
                    flexible += widths[i];
                }
            }

            if (deficit > 0 && flexible > deficit)
            {
                for (var i = 0; i < count; i++)
                {
                    if (widths[i] > minPt)
                    {
                        widths[i] -= deficit * (widths[i] / flexible);
                    }
                }
            }
        }

        var used = 0d;
        for (var i = 0; i < count - 1; i++)
        {
            widths[i] = Math.Round(widths[i], 2);
            used += widths[i];
        }

        widths[count - 1] = Math.Max(12, Math.Round(available - used, 2));
        return widths.Select(Unit.FromPoint).ToArray();
    }

    private static double[] MeasurePreferredWidths(ReportExportTable table, double available)
    {
        var count = table.Columns.Count;
        var styles = ExportHeaderBands.Aligned(table);
        var preferred = new double[count];
        using var measure = XGraphics.CreateMeasureContext(
            new XSize(4000, 200),
            XGraphicsUnit.Point,
            XPageDirection.Downwards);
        var body = new XFont("Tahoma", 8, XFontStyleEx.Regular);
        var headerFont = new XFont("Tahoma", 8, XFontStyleEx.Bold);
        const double pad = 10;
        var cap = Math.Max(78, available * 0.42);

        for (var i = 0; i < count; i++)
        {
            var widest = TextWidth(measure, table.Columns[i], headerFont);
            var style = i < styles.Count ? styles[i] : new ExportColumnStyle();
            if (!string.IsNullOrWhiteSpace(style.HeaderBand))
            {
                widest = Math.Max(widest, TextWidth(measure, style.HeaderBand, headerFont));
            }

            foreach (var rowIndex in SampleRows(table.Rows.Count))
            {
                var cells = table.Rows[rowIndex];
                var raw = i < cells.Count ? cells[i] : string.Empty;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                widest = Math.Max(widest, TextWidth(measure, raw, body));
            }

            var width = Math.Clamp(widest + pad, 28, cap);
            if (style.Wrap)
            {
                width = Math.Min(width, 110);
            }

            preferred[i] = width;
        }

        return preferred;
    }

    private static double TextWidth(XGraphics measure, string text, XFont font)
    {
        var sample = text.Length <= 160 ? text : text[..160];
        return measure.MeasureString(PdfArabicText.Prepare(sample), font).Width;
    }

    private static IEnumerable<int> SampleRows(int count)
    {
        const int max = 160;
        if (count <= max)
        {
            for (var i = 0; i < count; i++)
            {
                yield return i;
            }

            yield break;
        }

        var step = Math.Max(1, count / max);
        for (var i = 0; i < count; i += step)
        {
            yield return i;
        }
    }

    private static void StyleHeaderRow(Row row)
    {
        row.HeadingFormat = true;
        row.Shading.Color = new Color(0xFFB5511F);
        row.Format.Font.Color = Colors.White;
        row.Format.Font.Bold = true;
        row.Format.Font.Size = 8;
        row.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void FillCell(Cell cell, string text, string? align, bool wrap)
    {
        var shown = wrap ? PdfCellText(text) : PdfSingleLine(text);
        var paragraph = cell.AddParagraph(shown);
        var alignment = AlignmentFor(align, text);
        if (alignment is { } value)
        {
            paragraph.Format.Alignment = value;
        }

        cell.VerticalAlignment = wrap ? VerticalAlignment.Top : VerticalAlignment.Center;
    }

    private static ParagraphAlignment? AlignmentFor(string? align, string text)
        => align switch
        {
            "left" => ParagraphAlignment.Left,
            "center" => ParagraphAlignment.Center,
            "right" => ParagraphAlignment.Right,
            _ => PdfArabicText.ContainsArabic(text) ? ParagraphAlignment.Right : null
        };

    private static string PdfSingleLine(string? value)
    {
        var prepared = PdfArabicText.Prepare(value).Replace('\n', ' ').Replace('\r', ' ');
        return prepared.Replace(" ", "\u00A0", StringComparison.Ordinal);
    }

    private static string PdfCellText(string? value)
    {
        var prepared = PdfArabicText.Prepare(value);
        if (prepared.Length < 24 || PdfArabicText.ContainsArabic(prepared) || prepared.Contains(' ') || prepared.Contains('\n'))
        {
            return prepared;
        }

        var builder = new StringBuilder(prepared.Length + 8);
        for (var i = 0; i < prepared.Length; i++)
        {
            if (i > 0 && i % 18 == 0)
            {
                builder.Append('\u200B');
            }

            builder.Append(prepared[i]);
        }

        return builder.ToString();
    }

    private static bool HasLetterhead(ReportExportTable table)
        => table.Logo is { Length: > 0 }
            || !string.IsNullOrWhiteSpace(table.Organization)
            || !string.IsNullOrWhiteSpace(table.OrganizationArabic);

    private static string? AddLetterhead(Section section, ReportExportTable table, Unit contentWidth)
    {
        var header = section.Headers.Primary.AddTable();
        header.Borders.Width = 0;
        header.Rows.LeftIndent = 0;
        var logoWidth = Unit.FromMillimeter(36);
        var side = (contentWidth - logoWidth) / 2;
        header.AddColumn(side);
        header.AddColumn(logoWidth);
        header.AddColumn(side);

        var row = header.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        AddOrganization(row.Cells[0], table.Organization, ParagraphAlignment.Left);
        string? logoPath = null;
        if (table.Logo is { Length: > 0 })
        {
            logoPath = AddLogo(row.Cells[1], table.Logo);
        }

        AddOrganization(row.Cells[2], table.OrganizationArabic, ParagraphAlignment.Right);
        return logoPath;
    }

    private static void AddOrganization(Cell cell, string? name, ParagraphAlignment alignment)
    {
        var organization = name?.Trim();
        if (string.IsNullOrWhiteSpace(organization))
        {
            return;
        }

        var line = cell.AddParagraph(PdfArabicText.Prepare(organization));
        line.Format.Font.Size = 10;
        line.Format.Font.Bold = true;
        line.Format.Font.Color = new Color(0xFF211B2A);
        line.Format.Alignment = alignment;
        cell.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void AddReportTitle(Section section, ReportExportTable table, Unit contentWidth)
    {
        var english = table.Title.Trim();
        var arabic = table.TitleArabic?.Trim();
        if (!string.IsNullOrWhiteSpace(arabic)
            && arabic.Equals(english, StringComparison.Ordinal))
        {
            arabic = null;
        }

        if (string.IsNullOrWhiteSpace(english) && string.IsNullOrWhiteSpace(arabic))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(english) || string.IsNullOrWhiteSpace(arabic))
        {
            var only = string.IsNullOrWhiteSpace(english) ? arabic! : english;
            var heading = section.AddParagraph(PdfArabicText.Prepare(only));
            heading.Format.Font.Name = "Tahoma";
            heading.Format.Font.Size = TitleFontSize(only, null, contentWidth);
            heading.Format.Font.Bold = true;
            heading.Format.Font.Color = new Color(0xFF211B2A);
            heading.Format.Alignment = string.IsNullOrWhiteSpace(english)
                ? ParagraphAlignment.Right
                : ParagraphAlignment.Left;
            heading.Format.SpaceAfter = "2mm";
            return;
        }

        var size = TitleFontSize(english, arabic, contentWidth);
        var title = section.AddTable();
        title.Borders.Width = 0;
        title.Rows.LeftIndent = 0;
        title.Format.SpaceAfter = "2mm";
        var side = contentWidth / 2;
        title.AddColumn(side);
        title.AddColumn(side);
        var row = title.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        AddTitleCell(row.Cells[0], english, ParagraphAlignment.Left, size);
        AddTitleCell(row.Cells[1], arabic, ParagraphAlignment.Right, size);
    }

    private static void AddTitleCell(Cell cell, string name, ParagraphAlignment alignment, double size)
    {
        var line = cell.AddParagraph(PdfArabicText.Prepare(name));
        line.Format.Font.Name = "Tahoma";
        line.Format.Font.Size = size;
        line.Format.Font.Bold = true;
        line.Format.Font.Color = new Color(0xFF211B2A);
        line.Format.Alignment = alignment;
        line.Format.SpaceBefore = 0;
        line.Format.SpaceAfter = 0;
        cell.VerticalAlignment = VerticalAlignment.Center;
    }

    private static double TitleFontSize(string english, string? arabic, Unit contentWidth)
    {
        const double largest = 14;
        const double smallest = 7;
        var paired = !string.IsNullOrWhiteSpace(arabic);
        var width = contentWidth.Point / (paired ? 2 : 1) - 6;
        if (width < 40)
        {
            return smallest;
        }

        try
        {
            using var measure = XGraphics.CreateMeasureContext(
                new XSize(2000, 200),
                XGraphicsUnit.Point,
                XPageDirection.Downwards);
            for (var size = largest; size > smallest; size -= 0.5)
            {
                if (Fits(measure, english, size, width)
                    && (!paired || Fits(measure, arabic!, size, width)))
                {
                    return size;
                }
            }
        }
        catch (InvalidOperationException)
        {
            return 11;
        }

        return smallest;
    }

    private static bool Fits(XGraphics measure, string text, double size, double width)
    {
        var font = new XFont("Tahoma", size, XFontStyleEx.Bold);
        return measure.MeasureString(PdfArabicText.Prepare(text), font).Width <= width;
    }

    private static string DocumentTitle(ReportExportTable table)
    {
        var english = table.Title.Trim();
        var arabic = table.TitleArabic?.Trim();
        if (string.IsNullOrWhiteSpace(arabic) || arabic.Equals(english, StringComparison.Ordinal))
        {
            return english;
        }

        return string.IsNullOrWhiteSpace(english) ? arabic : $"{english} - {arabic}";
    }

    private static string? AddLogo(Cell cell, byte[] logo)
    {
        var extension = LogoExtension(logo);
        if (extension is null)
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), "rb-logo-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            File.WriteAllBytes(path, logo);
            var image = cell.AddImage(path);
            image.LockAspectRatio = true;
            image.Width = "28mm";
            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.Format.Alignment = ParagraphAlignment.Center;
            return path;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? LogoExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            return ".gif";
        }

        return null;
    }

    private static void AddFooter(Section section, ReportExportTable table, Unit contentWidth)
    {
        var organization = FooterOrganization(table);
        var footer = section.Footers.Primary;
        if (string.IsNullOrWhiteSpace(organization))
        {
            var pageLine = footer.AddParagraph();
            pageLine.Format.Font.Size = 8;
            pageLine.Format.Font.Color = new Color(0xFF6D6478);
            pageLine.Format.Alignment = table.Arabic ? ParagraphAlignment.Left : ParagraphAlignment.Right;
            AddPageNumbers(pageLine, table.Arabic);
            return;
        }

        var tableFooter = footer.AddTable();
        tableFooter.Borders.Width = 0;
        tableFooter.AddColumn(contentWidth / 2);
        tableFooter.AddColumn(contentWidth / 2);
        var row = tableFooter.AddRow();
        var orgCell = table.Arabic ? row.Cells[1] : row.Cells[0];
        var pageCell = table.Arabic ? row.Cells[0] : row.Cells[1];
        var org = orgCell.AddParagraph(PdfArabicText.Prepare(organization));
        org.Format.Font.Size = 8;
        org.Format.Font.Color = new Color(0xFF6D6478);
        org.Format.Alignment = table.Arabic ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        var numbers = pageCell.AddParagraph();
        numbers.Format.Font.Size = 8;
        numbers.Format.Font.Color = new Color(0xFF6D6478);
        numbers.Format.Alignment = table.Arabic ? ParagraphAlignment.Left : ParagraphAlignment.Right;
        AddPageNumbers(numbers, table.Arabic);
    }

    private static void AddPageNumbers(Paragraph paragraph, bool arabic)
    {
        if (arabic)
        {
            paragraph.AddText("صفحة ");
            paragraph.AddPageField();
            paragraph.AddText(" من ");
            paragraph.AddNumPagesField();
            return;
        }

        paragraph.AddText("Page ");
        paragraph.AddPageField();
        paragraph.AddText(" of ");
        paragraph.AddNumPagesField();
    }

    private static string? FooterOrganization(ReportExportTable table)
    {
        var english = table.Organization?.Trim();
        var arabic = table.OrganizationArabic?.Trim();
        if (table.Arabic)
        {
            return string.IsNullOrWhiteSpace(arabic) ? english : arabic;
        }

        return string.IsNullOrWhiteSpace(english) ? arabic : english;
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
