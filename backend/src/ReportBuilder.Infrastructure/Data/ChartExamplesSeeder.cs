using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Data;

public static class ChartExamplesSeeder
{
    public static readonly Guid FirmsByTypeId = Guid.Parse("8f3c1a10-6b2e-4d91-9c44-1f8e0a11c001");
    public static readonly Guid FirmsByStatusId = Guid.Parse("8f3c1a10-6b2e-4d91-9c44-1f8e0a11c002");
    public static readonly Guid LicenseExpiryId = Guid.Parse("8f3c1a10-6b2e-4d91-9c44-1f8e0a11c003");
    public static readonly Guid TypeAndStatusId = Guid.Parse("8f3c1a10-6b2e-4d91-9c44-1f8e0a11c004");
    public static readonly Guid EvaluationByTypeId = Guid.Parse("8f3c1a10-6b2e-4d91-9c44-1f8e0a11c005");

    public static async Task SeedAsync(ReportBuilderDbContext db, CancellationToken cancellationToken = default)
    {
        var datasetId = await db.Datasets.AsNoTracking()
            .Where(dataset => dataset.SchemaName == "dbo"
                && dataset.ObjectName == "RPT_FIRM_DETAILS"
                && dataset.IsEnabled)
            .Select(dataset => dataset.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (datasetId == Guid.Empty)
        {
            return;
        }

        var adminId = await db.Users.AsNoTracking()
            .Where(user => user.UserName == "admin")
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var existing = (await db.ReportTemplates.AsNoTracking()
            .Where(template =>
                template.Id == FirmsByTypeId
                || template.Id == FirmsByStatusId
                || template.Id == LicenseExpiryId
                || template.Id == TypeAndStatusId
                || template.Id == EvaluationByTypeId)
            .Select(template => template.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        foreach (var example in Examples(datasetId, adminId, now))
        {
            if (existing.Contains(example.Id))
            {
                continue;
            }

            var status = example.Status.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [rb].[ReportTemplates]
                    ([Id], [Name], [NameArabic], [Description], [DescriptionArabic], [DatasetId], [LayoutJson], [Status], [CreatedBy], [IsAdminCreated], [IsRestricted], [CreatedAt], [UpdatedAt])
                VALUES
                    ({example.Id}, {example.Name}, {example.NameArabic}, {example.Description}, {example.DescriptionArabic}, {example.DatasetId}, {example.LayoutJson}, {status}, {example.CreatedBy}, {example.IsAdminCreated}, {example.IsRestricted}, {example.CreatedAt}, {example.UpdatedAt})
                """, cancellationToken);
        }
    }

    private static IEnumerable<ReportTemplate> Examples(Guid datasetId, Guid? adminId, DateTimeOffset now)
    {
        yield return Template(
            FirmsByTypeId,
            "Firms by type",
            "الشركات حسب النوع",
            "Count of classified firms grouped by firm type.",
            "عدد الشركات المصنفة مجمّعة حسب نوع الشركة.",
            datasetId,
            adminId,
            now,
            Bar("FirmType"));

        yield return Template(
            FirmsByStatusId,
            "Firms by status",
            "الشركات حسب الحالة",
            "Share of firms in each license status.",
            "حصة الشركات في كل حالة رخصة.",
            datasetId,
            adminId,
            now,
            Pie("FirmStatus"));

        yield return Template(
            LicenseExpiryId,
            "License expiry status",
            "حالة انتهاء الرخصة",
            "Count of firms by trade-license expiry status.",
            "عدد الشركات حسب حالة انتهاء الرخصة التجارية.",
            datasetId,
            adminId,
            now,
            Doughnut("TradeLicenseExpiryStatus"));

        yield return Template(
            TypeAndStatusId,
            "Firms by type and status",
            "الشركات حسب النوع والحالة",
            "Stacked count of firms by type, split by status.",
            "عدد مكدس للشركات حسب النوع، مقسوم حسب الحالة.",
            datasetId,
            adminId,
            now,
            StackedBar("FirmType", "FirmStatus"));

        yield return Template(
            EvaluationByTypeId,
            "Average evaluation by type",
            "متوسط التقييم حسب النوع",
            "Average annual evaluation for each firm type.",
            "متوسط التقييم السنوي لكل نوع شركة.",
            datasetId,
            adminId,
            now,
            BarMeasure("FirmType", "FirmAnnualEvalution", "AVG", "Average evaluation"));
    }

    private static ReportTemplate Template(
        Guid id,
        string name,
        string nameArabic,
        string description,
        string descriptionArabic,
        Guid datasetId,
        Guid? adminId,
        DateTimeOffset now,
        ReportLayout layout)
        => new()
        {
            Id = id,
            Name = name,
            NameArabic = nameArabic,
            Description = description,
            DescriptionArabic = descriptionArabic,
            DatasetId = datasetId,
            LayoutJson = LayoutJson.Serialize(layout),
            Status = ReportStatus.Published,
            CreatedBy = adminId,
            IsAdminCreated = true,
            IsRestricted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static ReportLayout Bar(string category)
        => ChartLayout("bar", category, CountMeasure(), stacked: false);

    private static ReportLayout Pie(string category)
        => ChartLayout("pie", category, CountMeasure(), stacked: false);

    private static ReportLayout Doughnut(string category)
        => ChartLayout("doughnut", category, CountMeasure(), stacked: false);

    private static ReportLayout StackedBar(string category, string series)
        => ChartLayout("bar", category, CountMeasure(), stacked: true, series: series);

    private static ReportLayout BarMeasure(string category, string field, string aggregate, string alias)
        => ChartLayout("bar", category, new LayoutColumn
        {
            Field = field,
            Aggregate = aggregate,
            Alias = alias
        }, stacked: false);

    private static LayoutColumn CountMeasure()
        => new()
        {
            Field = "FirmNumber",
            Aggregate = "COUNT",
            Alias = "Firms"
        };

    private static ReportLayout ChartLayout(
        string type,
        string category,
        LayoutColumn measure,
        bool stacked,
        string? series = null)
    {
        var groupBy = string.IsNullOrWhiteSpace(series)
            ? new List<string> { category }
            : [category, series];
        var columns = new List<LayoutColumn> { new() { Field = category } };
        if (!string.IsNullOrWhiteSpace(series))
        {
            columns.Add(new LayoutColumn { Field = series });
        }

        columns.Add(measure);
        return new ReportLayout
        {
            Kind = "chart",
            Columns = columns,
            GroupBy = groupBy,
            Filters = [],
            OrderBy = [],
            Chart = new ChartConfig
            {
                Type = type,
                Stacked = stacked,
                ShowLegend = true,
                ShowDataLabels = type is "pie" or "doughnut"
            }
        };
    }
}
