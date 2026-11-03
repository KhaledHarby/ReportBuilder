using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Data;

public static class AnalyticsExamplesSeeder
{
    public static readonly Guid FirmLandscapeId = Guid.Parse("9a4d2c10-6b2e-4d91-9c44-1f8e0a22d001");

    public static async Task<IReadOnlyList<string>> SeedAsync(
        ReportBuilderDbContext db,
        CancellationToken cancellationToken = default)
    {
        var enabled = await db.Datasets.AsNoTracking()
            .Include(dataset => dataset.Columns)
            .Where(dataset => dataset.IsEnabled)
            .OrderBy(dataset => dataset.SortOrder)
            .ThenBy(dataset => dataset.DisplayName)
            .ToListAsync(cancellationToken);

        if (enabled.Count == 0)
        {
            return ["No enabled datasets in the catalog."];
        }

        var adminId = await db.Users.AsNoTracking()
            .Where(user => user.UserName == "admin")
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var existingIds = (await db.AnalyticsDashboards.AsNoTracking()
            .Select(item => item.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var notes = new List<string>
        {
            $"Enabled datasets: {enabled.Count}"
        };
        foreach (var dataset in enabled)
        {
            var visible = dataset.Columns.Count(column => !column.IsHidden);
            notes.Add($"- {dataset.SchemaName}.{dataset.ObjectName} · {dataset.DisplayName} · {visible} columns");
        }

        var now = DateTimeOffset.UtcNow;
        var created = 0;

        var firms = enabled.FirstOrDefault(dataset =>
            dataset.ObjectName.Equals("RPT_FIRMS", StringComparison.OrdinalIgnoreCase)
            || dataset.ObjectName.Equals("RPT_FIRM_DETAILS", StringComparison.OrdinalIgnoreCase)
            || dataset.ObjectName.Contains("FIRM_DETAIL", StringComparison.OrdinalIgnoreCase));
        if (firms is not null && TryBuildFirmDashboard(firms, out var firmConfig))
        {
            var result = await UpsertAsync(db, existingIds, DashboardIdFor(firms.Id), adminId, now, firmConfig,
                    "Firm landscape",
                    "مشهد الشركات",
                    "Associative view of classified firms: type, status, license expiry, and evaluation.",
                    "عرض ارتباطي للشركات المصنفة: النوع والحالة وانتهاء الرخصة والتقييم.",
                    cancellationToken);
            notes.Add($"{result}: Firm landscape");
            if (result == "Created")
            {
                created++;
            }
        }

        foreach (var dataset in enabled.Where(item => item.Id != firms?.Id).Take(6))
        {
            var built = dataset.ObjectName.Equals("RPT_FIRMS_ENGINEERS", StringComparison.OrdinalIgnoreCase)
                ? TryBuildEngineersDashboard(dataset, out var config)
                : dataset.ObjectName.Equals("RPT_USER_ACTIVE_TASKS", StringComparison.OrdinalIgnoreCase)
                    ? TryBuildTasksDashboard(dataset, out config)
                    : TryBuildGenericDashboard(dataset, out config);
            if (!built)
            {
                notes.Add($"Skipped {dataset.ObjectName}: not enough visible fields for a starter dashboard.");
                continue;
            }

            var id = DashboardIdFor(dataset.Id);
            var title = dataset.ObjectName.Equals("RPT_FIRMS_ENGINEERS", StringComparison.OrdinalIgnoreCase)
                ? "Firm engineers"
                : dataset.ObjectName.Equals("RPT_USER_ACTIVE_TASKS", StringComparison.OrdinalIgnoreCase)
                    ? "Active user tasks"
                    : $"{dataset.DisplayName} explorer";
            var titleAr = dataset.ObjectName.Equals("RPT_FIRMS_ENGINEERS", StringComparison.OrdinalIgnoreCase)
                ? "مهندسو الشركات"
                : dataset.ObjectName.Equals("RPT_USER_ACTIVE_TASKS", StringComparison.OrdinalIgnoreCase)
                    ? "مهام المستخدمين"
                    : dataset.DisplayNameArabic;
            var result = await UpsertAsync(db, existingIds, id, adminId, now, config,
                    title,
                    titleAr,
                    $"Starter analytics for {dataset.SchemaName}.{dataset.ObjectName}.",
                    dataset.DisplayNameArabic is null
                        ? null
                        : $"تحليلات أولية لـ {dataset.DisplayNameArabic}.",
                    cancellationToken);
            notes.Add($"{result}: {title}");
            if (result == "Created")
            {
                created++;
            }
        }

        notes.Add(created == 0 ? "No new analytics dashboards were added." : $"Added {created} analytics dashboard(s).");
        return notes;
    }

    private static async Task<string> UpsertAsync(
        ReportBuilderDbContext db,
        HashSet<Guid> existingIds,
        Guid id,
        Guid? adminId,
        DateTimeOffset now,
        AnalyticsDashboardConfig config,
        string name,
        string? nameArabic,
        string? description,
        string? descriptionArabic,
        CancellationToken cancellationToken)
    {
        var json = AnalyticsConfigJson.Serialize(config);
        if (existingIds.Contains(id))
        {
            var current = await db.AnalyticsDashboards.FirstAsync(item => item.Id == id, cancellationToken);
            current.Name = name;
            current.NameArabic = EmptyToNull(nameArabic);
            current.Description = EmptyToNull(description);
            current.DescriptionArabic = EmptyToNull(descriptionArabic);
            current.ConfigJson = json;
            current.Status = ReportStatus.Published;
            current.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return "Updated";
        }

        db.AnalyticsDashboards.Add(new AnalyticsDashboard
        {
            Id = id,
            Name = name,
            NameArabic = EmptyToNull(nameArabic),
            Description = EmptyToNull(description),
            DescriptionArabic = EmptyToNull(descriptionArabic),
            ConfigJson = json,
            Status = ReportStatus.Published,
            IsRestricted = false,
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        existingIds.Add(id);
        return "Created";
    }

    private static bool TryBuildFirmDashboard(Dataset dataset, out AnalyticsDashboardConfig config)
    {
        config = new AnalyticsDashboardConfig();
        var sourceId = "src_firms";
        var type = FindColumn(dataset, "FirmType", "Type");
        var status = FindColumn(dataset, "FirmStatus", "Status", "LicenseStatus");
        var expiry = FindColumn(dataset, "TradeLicenseExpiryStatus", "ExpiryStatus", "LicenseExpiryStatus", "FirmClassificationStatus");
        var classification = FindColumn(dataset, "FirmClassificationCategory", "Classification", "FirmClassification", "ClassCategory", "Category");
        var city = FindColumn(dataset, "FirmCity", "City", "Municipality");
        var place = FindColumn(dataset, "FirmIssuePlace", "IssuePlace");
        var evalCol = FindColumn(dataset, "FirmAnnualEvalution", "FirmAnnualEvaluation", "AnnualEvaluation");
        var equity = FindColumn(dataset, "FirmFinancialNetEquity", "NetEquity");
        var expiryDate = FindColumn(dataset, "FirmExpiryDate", "TradeLicenseExpiryDate");
        if (type is null && status is null)
        {
            return TryBuildGenericDashboard(dataset, out config);
        }

        var dimensions = new List<AnalyticsDimension>();
        AddDimension(dimensions, sourceId, type, "dim_type", "Firm type");
        AddDimension(dimensions, sourceId, status, "dim_status", "Firm status");
        AddDimension(dimensions, sourceId, classification, "dim_class", "Classification");
        AddDimension(dimensions, sourceId, expiry, "dim_class_status", "Classification status");
        AddDimension(dimensions, sourceId, city, "dim_city", "Municipality");
        AddDimension(dimensions, sourceId, place, "dim_place", "Issue place");
        if (expiryDate is not null)
        {
            dimensions.Add(new AnalyticsDimension
            {
                Id = "dim_expiry_year",
                SourceId = sourceId,
                Field = expiryDate.ColumnName,
                Expression = $"YEAR([{expiryDate.ColumnName}])",
                Label = "Expiry year"
            });
        }
        if (dimensions.Count == 0)
        {
            return false;
        }

        var measures = new List<AnalyticsMeasure>
        {
            new()
            {
                Id = "msr_count",
                SourceId = sourceId,
                Field = FindColumn(dataset, "FirmNumber", "FirmId")?.ColumnName,
                Aggregation = "count",
                Label = "Firms"
            }
        };
        if (evalCol is not null)
        {
            measures.Add(new AnalyticsMeasure
            {
                Id = "msr_eval",
                SourceId = sourceId,
                Field = evalCol.ColumnName,
                Aggregation = "avg",
                Label = "Average evaluation",
                Format = "0.0"
            });
        }
        if (equity is not null)
        {
            measures.Add(new AnalyticsMeasure
            {
                Id = "msr_equity",
                SourceId = sourceId,
                Field = equity.ColumnName,
                Aggregation = "sum",
                Label = "Net equity"
            });
        }

        var charts = new List<AnalyticsChart>();
        if (type is not null)
        {
            charts.Add(Chart("cht_type", "bar", "dim_type", "msr_count", "Firms by type"));
        }
        if (status is not null)
        {
            charts.Add(Chart("cht_status", "pie", "dim_status", "msr_count", "Firms by status"));
        }
        if (classification is not null)
        {
            charts.Add(Chart("cht_class", "bar", "dim_class", "msr_count", "Firms by classification"));
        }
        if (city is not null)
        {
            charts.Add(Chart("cht_city", "pie", "dim_city", "msr_count", "Firms by municipality"));
        }
        if (evalCol is not null && type is not null)
        {
            charts.Add(Chart("cht_eval", "bar", "dim_type", "msr_eval", "Average evaluation by type"));
        }

        config = new AnalyticsDashboardConfig
        {
            Version = 1,
            Sources = [new AnalyticsSource { Id = sourceId, DatasetId = dataset.Id, Alias = dataset.DisplayName }],
            Dimensions = dimensions,
            Measures = measures,
            DetailColumns = DetailColumns(dataset, sourceId, "FirmEnglishName", "FirmArabicName", "FirmNameEn",
                "FirmType", "FirmStatus", "FirmClassificationCategory", "FirmClassificationStatus", "FirmCity",
                "FirmExpiryDate", "FirmAnnualEvalution", "FirmNumber"),
            Charts = charts,
            Map = TryMap(dataset, sourceId)
        };
        return true;
    }

    private static bool TryBuildEngineersDashboard(Dataset dataset, out AnalyticsDashboardConfig config)
    {
        var role = FindColumn(dataset, "MECS_ROLE_NAME_E", "Role", "MECS_ROLE_NAME");
        if (role is null)
        {
            return TryBuildGenericDashboard(dataset, out config);
        }

        const string sourceId = "src_engineers";
        var experience = FindColumn(dataset, "TOTAL_EXPERIENCE", "Experience");
        config = new AnalyticsDashboardConfig
        {
            Version = 1,
            Sources = [new AnalyticsSource { Id = sourceId, DatasetId = dataset.Id, Alias = "Engineers" }],
            Dimensions =
            [
                new AnalyticsDimension
                {
                    Id = "dim_role",
                    SourceId = sourceId,
                    Field = role.ColumnName,
                    Label = "Role"
                }
            ],
            Measures =
            [
                new AnalyticsMeasure { Id = "msr_count", SourceId = sourceId, Aggregation = "count", Label = "Engineers" },
                .. experience is null
                    ? Array.Empty<AnalyticsMeasure>()
                    : [new AnalyticsMeasure
                    {
                        Id = "msr_exp",
                        SourceId = sourceId,
                        Field = experience.ColumnName,
                        Aggregation = "avg",
                        Label = "Average experience",
                        Format = "0.0"
                    }]
            ],
            DetailColumns = DetailColumns(dataset, sourceId, "Engineer_Full_Name_E", "MECS_ROLE_NAME_E",
                "Engineer_License_Number", "Trade_License_Number", "TOTAL_EXPERIENCE"),
            Charts =
            [
                Chart("cht_role", "bar", "dim_role", "msr_count", "Engineers by role"),
                .. experience is null
                    ? Array.Empty<AnalyticsChart>()
                    : [Chart("cht_exp", "bar", "dim_role", "msr_exp", "Average experience by role")]
            ]
        };
        return true;
    }

    private static bool TryBuildTasksDashboard(Dataset dataset, out AnalyticsDashboardConfig config)
    {
        var status = FindColumn(dataset, "Status");
        if (status is null)
        {
            return TryBuildGenericDashboard(dataset, out config);
        }

        const string sourceId = "src_tasks";
        var due = FindColumn(dataset, "DuePercentage");
        config = new AnalyticsDashboardConfig
        {
            Version = 1,
            Sources = [new AnalyticsSource { Id = sourceId, DatasetId = dataset.Id, Alias = "Tasks" }],
            Dimensions =
            [
                new AnalyticsDimension
                {
                    Id = "dim_status",
                    SourceId = sourceId,
                    Field = status.ColumnName,
                    Label = "Status"
                },
                .. FindColumn(dataset, "UserID") is { } user
                    ? [new AnalyticsDimension
                    {
                        Id = "dim_user",
                        SourceId = sourceId,
                        Field = user.ColumnName,
                        Label = "User"
                    }]
                    : Array.Empty<AnalyticsDimension>()
            ],
            Measures =
            [
                new AnalyticsMeasure { Id = "msr_count", SourceId = sourceId, Aggregation = "count", Label = "Tasks" },
                .. due is null
                    ? Array.Empty<AnalyticsMeasure>()
                    : [new AnalyticsMeasure
                    {
                        Id = "msr_due",
                        SourceId = sourceId,
                        Field = due.ColumnName,
                        Aggregation = "avg",
                        Label = "Average due %",
                        Format = "0"
                    }]
            ],
            DetailColumns = DetailColumns(dataset, sourceId, "AssignedTaskID", "UserID", "Status", "DueDate", "DuePercentage"),
            Charts =
            [
                Chart("cht_status", "pie", "dim_status", "msr_count", "Tasks by status")
            ]
        };
        return true;
    }

    private static bool TryBuildGenericDashboard(Dataset dataset, out AnalyticsDashboardConfig config)
    {
        config = new AnalyticsDashboardConfig();
        var visible = dataset.Columns
            .Where(column => !column.IsHidden)
            .OrderBy(column => column.Ordinal)
            .ToList();
        if (visible.Count == 0)
        {
            return false;
        }

        var sourceId = "src_main";
        var dimensionCols = PickDimensions(visible);
        if (dimensionCols.Count == 0)
        {
            return false;
        }

        var dimensions = dimensionCols
            .Select((column, index) => new AnalyticsDimension
            {
                Id = $"dim_{index + 1}",
                SourceId = sourceId,
                Field = column.ColumnName,
                Label = LabelOf(column)
            })
            .ToList();

        var measures = new List<AnalyticsMeasure>
        {
            new()
            {
                Id = "msr_count",
                SourceId = sourceId,
                Aggregation = "count",
                Label = "Rows"
            }
        };
        var numeric = visible.FirstOrDefault(IsNumericMeasure);
        if (numeric is not null)
        {
            measures.Add(new AnalyticsMeasure
            {
                Id = "msr_sum",
                SourceId = sourceId,
                Field = numeric.ColumnName,
                Aggregation = "sum",
                Label = LabelOf(numeric)
            });
        }

        var charts = new List<AnalyticsChart>
        {
            Chart("cht_bar", "bar", dimensions[0].Id, "msr_count", $"{LabelOf(dimensionCols[0])} mix")
        };
        if (dimensions.Count > 1)
        {
            charts.Add(Chart("cht_pie", "pie", dimensions[1].Id, "msr_count", LabelOf(dimensionCols[1])));
        }

        config = new AnalyticsDashboardConfig
        {
            Version = 1,
            Sources = [new AnalyticsSource { Id = sourceId, DatasetId = dataset.Id, Alias = dataset.DisplayName }],
            Dimensions = dimensions,
            Measures = measures,
            DetailColumns = visible.Take(8).Select((column, index) => new AnalyticsDetailColumn
            {
                Id = $"col_{index + 1}",
                SourceId = sourceId,
                Field = column.ColumnName,
                Label = LabelOf(column)
            }).ToList(),
            Charts = charts,
            Map = TryMap(dataset, sourceId)
        };
        return true;
    }

    private static List<DatasetColumn> PickDimensions(IReadOnlyList<DatasetColumn> visible)
    {
        var preferred = visible.Where(column =>
                column.IsGroupable
                && IsText(column)
                && LooksCategorical(column.ColumnName))
            .Take(5)
            .ToList();
        if (preferred.Count > 0)
        {
            return preferred;
        }

        return visible
            .Where(column => column.IsGroupable && IsText(column) && !LooksLikeIdentifier(column.ColumnName))
            .Take(4)
            .ToList();
    }

    private static void AddDimension(
        List<AnalyticsDimension> dimensions,
        string sourceId,
        DatasetColumn? column,
        string id,
        string label)
    {
        if (column is null)
        {
            return;
        }

        dimensions.Add(new AnalyticsDimension
        {
            Id = id,
            SourceId = sourceId,
            Field = column.ColumnName,
            Label = string.IsNullOrWhiteSpace(column.DisplayName) ? label : column.DisplayName
        });
    }

    private static List<AnalyticsDetailColumn> DetailColumns(Dataset dataset, string sourceId, params string[] names)
    {
        var list = new List<AnalyticsDetailColumn>();
        foreach (var name in names)
        {
            var column = FindColumn(dataset, name);
            if (column is null || list.Any(item => item.Field.Equals(column.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            list.Add(new AnalyticsDetailColumn
            {
                Id = $"col_{list.Count + 1}",
                SourceId = sourceId,
                Field = column.ColumnName,
                Label = LabelOf(column)
            });
        }

        if (list.Count == 0)
        {
            list.AddRange(dataset.Columns.Where(column => !column.IsHidden).Take(6).Select((column, index) =>
                new AnalyticsDetailColumn
                {
                    Id = $"col_{index + 1}",
                    SourceId = sourceId,
                    Field = column.ColumnName,
                    Label = LabelOf(column)
                }));
        }

        return list;
    }

    private static AnalyticsMap? TryMap(Dataset dataset, string sourceId)
    {
        var lat = dataset.Columns.FirstOrDefault(column =>
            !column.IsHidden && LooksLike(column.ColumnName, "lat", "latitude"));
        var lng = dataset.Columns.FirstOrDefault(column =>
            !column.IsHidden && LooksLike(column.ColumnName, "lng", "lon", "long", "longitude"));
        if (lat is null || lng is null)
        {
            return null;
        }

        var label = FindColumn(dataset, "FirmNameEn", "NameEn", "Name", "Title", "Label") ?? lat;
        return new AnalyticsMap
        {
            Enabled = true,
            SourceId = sourceId,
            LatField = lat.ColumnName,
            LngField = lng.ColumnName,
            LabelField = label.ColumnName
        };
    }

    private static AnalyticsChart Chart(string id, string type, string dimensionId, string measureId, string title)
        => new()
        {
            Id = id,
            Type = type,
            DimensionId = dimensionId,
            MeasureId = measureId,
            Title = title,
            State = "default"
        };

    private static DatasetColumn? FindColumn(Dataset dataset, params string[] names)
        => dataset.Columns.FirstOrDefault(column =>
            !column.IsHidden
            && names.Any(name => column.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static bool IsText(DatasetColumn column)
    {
        var type = column.SqlType.ToLowerInvariant();
        return type.Contains("char") || type.Contains("text") || type.Contains("ntext");
    }

    private static bool IsNumericMeasure(DatasetColumn column)
    {
        if (LooksLikeIdentifier(column.ColumnName))
        {
            return false;
        }

        var type = column.SqlType.ToLowerInvariant();
        return type.Contains("int")
            || type.Contains("decimal")
            || type.Contains("numeric")
            || type.Contains("float")
            || type.Contains("real")
            || type.Contains("money");
    }

    private static bool LooksCategorical(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("type")
            || value.Contains("status")
            || value.Contains("category")
            || value.Contains("class")
            || value.Contains("region")
            || value.Contains("city")
            || value.Contains("emirate")
            || value.Contains("office")
            || value.Contains("gender")
            || value.Contains("national")
            || value.Contains("expiry")
            || value.Contains("state")
            || value.Contains("role")
            || value.Contains("place")
            || value.Contains("municip");
    }

    private static bool LooksLikeIdentifier(string name)
    {
        var value = name.ToLowerInvariant();
        return value is "id" or "guid" or "uuid"
            || value.EndsWith("id")
            || value.Contains("guid")
            || value.Contains("hash")
            || value.Contains("rowversion");
    }

    private static bool LooksLike(string columnName, params string[] tokens)
    {
        var value = columnName.ToLowerInvariant();
        return tokens.Any(token => value == token || value.EndsWith(token) || value.Contains(token));
    }

    private static string LabelOf(DatasetColumn column)
        => string.IsNullOrWhiteSpace(column.DisplayName) ? column.ColumnName : column.DisplayName;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Guid DashboardIdFor(Guid datasetId)
    {
        var bytes = datasetId.ToByteArray();
        bytes[0] = 0xA1;
        bytes[1] = 0x7C;
        bytes[2] = 0x01;
        return new Guid(bytes);
    }
}
