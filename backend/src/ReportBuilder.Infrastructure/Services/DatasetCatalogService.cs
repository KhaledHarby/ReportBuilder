using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class DatasetCatalogService : IDatasetCatalogService
{
    private readonly ReportBuilderDbContext _db;
    private readonly SqlSchemaReader _schema;
    private readonly IActivityLog _log;
    private readonly IDatasetCacheService _cache;

    public DatasetCatalogService(ReportBuilderDbContext db, IActivityLog log, IDatasetCacheService cache)
    {
        _db = db;
        _schema = new SqlSchemaReader(db);
        _log = log;
        _cache = cache;
    }

    public async Task<IReadOnlyList<DatasetLiveColumn>> PreviewQueryColumnsAsync(
        string? sqlText,
        CancellationToken cancellationToken = default)
    {
        var columns = await _schema.ProbeQueryColumnsAsync(sqlText, cancellationToken);
        return columns
            .Select(column => new DatasetLiveColumn
            {
                ColumnName = column.Name,
                SqlType = column.SqlType,
                Ordinal = column.Ordinal
            })
            .ToList();
    }

    public async Task<Dataset> CreateQueryAsync(DatasetQueryWriteRequest request, CancellationToken cancellationToken = default)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0)
        {
            throw new ReportQueryException("Display name is required.");
        }

        if (displayName.Length > 256)
        {
            throw new ReportQueryException("Display name must be 256 characters or fewer.");
        }

        var displayNameArabic = string.IsNullOrWhiteSpace(request.DisplayNameArabic)
            ? null
            : request.DisplayNameArabic.Trim();
        if (displayNameArabic is { Length: > 256 })
        {
            throw new ReportQueryException("Arabic display name must be 256 characters or fewer.");
        }

        var sql = ReadOnlySelect.Normalize(request.SqlText);
        var columns = await _schema.ProbeQueryColumnsAsync(sql, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var dataset = new Dataset
        {
            Id = id,
            SchemaName = DatasetSource.QuerySchema,
            ObjectName = id.ToString("N"),
            ObjectType = DatasetObjectType.Query,
            SqlText = sql,
            DisplayName = displayName,
            DisplayNameArabic = displayNameArabic,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var column in columns)
        {
            dataset.Columns.Add(new DatasetColumn
            {
                Id = Guid.NewGuid(),
                DatasetId = id,
                ColumnName = column.Name,
                SqlType = column.SqlType,
                DisplayName = column.Name,
                IsFilterable = true,
                IsGroupable = true,
                DefaultSortDirection = SortDirections.Desc,
                Ordinal = column.Ordinal
            });
        }

        _db.Datasets.Add(dataset);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.query.create",
                Message = $"Added SQL dataset {displayName}.",
                EntityType = "Dataset",
                EntityId = id,
                EntityName = displayName
            },
            cancellationToken);
        _db.ChangeTracker.Clear();
        return await Datasets().FirstAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Dataset>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await _schema.ListObjectsAsync(cancellationToken);
        var existing = await _db.Datasets.Include(d => d.Columns).ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(
            d => $"{d.SchemaName}.{d.ObjectName}",
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in discovered)
        {
            var key = $"{item.SchemaName}.{item.ObjectName}";
            if (!byKey.TryGetValue(key, out var dataset))
            {
                dataset = new Dataset
                {
                    Id = Guid.NewGuid(),
                    SchemaName = item.SchemaName,
                    ObjectName = item.ObjectName,
                    ObjectType = item.ObjectType,
                    DisplayName = item.ObjectName,
                    IsEnabled = false,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _db.Datasets.Add(dataset);
                byKey[key] = dataset;
            }
            else
            {
                if (dataset.ObjectType == DatasetObjectType.Query)
                {
                    continue;
                }

                dataset.ObjectType = item.ObjectType;
                dataset.UpdatedAt = DateTimeOffset.UtcNow;
                if (dataset.IsEnabled)
                {
                    await _schema.RefreshColumnsAsync(dataset, cancellationToken);
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.discover",
                Message = $"Catalog discover finished with {discovered.Count} objects."
            },
            cancellationToken);
        return await ListAsync(enabledOnly: false, cancellationToken);
    }

    public async Task<IReadOnlyList<Dataset>> ListAsync(bool enabledOnly, CancellationToken cancellationToken = default)
    {
        var query = Datasets();
        if (enabledOnly)
        {
            query = query.Where(d => d.IsEnabled);
        }

        return await query
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.DisplayName)
            .ThenBy(d => d.ObjectName)
            .ToListAsync(cancellationToken);
    }

    public Task<Dataset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Datasets().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<DatasetColumnGap?> GetColumnGapAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dataset = await _db.Datasets.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return dataset is null ? null : await _schema.GetColumnGapAsync(dataset, cancellationToken);
    }

    public async Task<DatasetSqlScript?> GetSqlScriptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dataset = await _db.Datasets.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dataset is null)
        {
            return null;
        }

        var script = await _schema.GetObjectScriptAsync(dataset, cancellationToken);
        return new DatasetSqlScript
        {
            DatasetId = dataset.Id,
            SchemaName = dataset.SchemaName,
            ObjectName = dataset.ObjectName,
            ObjectType = dataset.ObjectType.ToString(),
            Script = script
        };
    }

    public async Task<DatasetSqlScript> ApplySqlScriptAsync(
        Guid id,
        ApplyDatasetSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataset = await _db.Datasets.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        if (dataset.ObjectType == DatasetObjectType.Query)
        {
            return await ApplyQueryAsync(dataset, request.Script, cancellationToken);
        }

        await _schema.ApplyObjectScriptAsync(dataset, request.Script, cancellationToken);

        if (dataset.IsEnabled)
        {
            try
            {
                await _schema.RefreshColumnsAsync(dataset, cancellationToken);
            }
            catch (ReportQueryException)
            {
                // Object may still exist with a temporary column gap; keep apply success.
            }
        }

        dataset.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.sql.apply",
                Message = $"Applied SQL to {dataset.SchemaName}.{dataset.ObjectName}.",
                EntityType = "Dataset",
                EntityId = dataset.Id,
                EntityName = dataset.DisplayName,
                Details = $"{{\"objectType\":\"{dataset.ObjectType}\"}}"
            },
            cancellationToken);

        var script = await _schema.GetObjectScriptAsync(dataset, cancellationToken);
        return new DatasetSqlScript
        {
            DatasetId = dataset.Id,
            SchemaName = dataset.SchemaName,
            ObjectName = dataset.ObjectName,
            ObjectType = dataset.ObjectType.ToString(),
            Script = script
        };
    }

    public async Task<Dataset> ImportColumnsAsync(
        Guid id,
        ImportColumnsRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataset = await _db.Datasets.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        var imported = await _schema.ImportMissingColumnsAsync(dataset, request.ColumnNames, cancellationToken);
        var updatedAt = DateTimeOffset.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [rb].[Datasets]
            SET [UpdatedAt] = {updatedAt}
            WHERE [Id] = {id}
            """, cancellationToken);

        var names = request.ColumnNames is { Count: > 0 }
            ? string.Join(", ", request.ColumnNames.Where(name => !string.IsNullOrWhiteSpace(name)).Take(40))
            : "all missing";
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.import-columns",
                Message = imported == 0
                    ? $"No new columns to import for {dataset.DisplayName}."
                    : $"Imported {imported} column(s) into {dataset.DisplayName}.",
                EntityType = "Dataset",
                EntityId = id,
                EntityName = dataset.DisplayName,
                Details = $"{{\"imported\":{imported},\"columns\":\"{names.Replace("\"", "'")}\"}}"
            },
            cancellationToken);

        _db.ChangeTracker.Clear();
        return await Datasets().FirstAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportPerson>> ListPeopleAsync(CancellationToken cancellationToken = default)
        => await _db.Users.AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.UserName)
            .Select(user => new ReportPerson
            {
                Id = user.Id,
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                DisplayNameArabic = user.DisplayNameArabic
            })
            .ToListAsync(cancellationToken);

    public async Task<Dataset> UpdateAsync(Guid id, DatasetUpdateRequest request, CancellationToken cancellationToken = default)
    {
        _db.ChangeTracker.Clear();
        var dataset = await _db.Datasets
            .Include(d => d.Columns)
            .Include(d => d.Access)
            .Include(d => d.RoleAccess)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        var wasEnabled = dataset.IsEnabled;
        var enabling = request.IsEnabled == true && !wasEnabled;

        if (request.IsEnabled == true && (enabling || dataset.Columns.Count == 0))
        {
            if (dataset.ObjectType == DatasetObjectType.Query)
            {
                await GuardRemovedColumnsAsync(dataset, cancellationToken);
            }

            await _schema.RefreshColumnsAsync(dataset, cancellationToken);
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? dataset.DisplayName
            : request.DisplayName.Trim();
        var displayNameArabic = OptionalArabic(request.DisplayNameArabic, dataset.DisplayNameArabic);
        var isEnabled = request.IsEnabled ?? dataset.IsEnabled;
        var accessMode = string.IsNullOrWhiteSpace(request.AccessMode) && request.IsRestricted is null
            ? ReportAccessModes.Normalize(dataset.AccessMode, dataset.IsRestricted)
            : ReportAccessModes.Normalize(request.AccessMode, request.IsRestricted ?? dataset.IsRestricted);
        var isRestricted = accessMode != ReportAccessModes.Everyone;
        var sortOrder = request.SortOrder ?? dataset.SortOrder;
        var updatedAt = DateTimeOffset.UtcNow;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [rb].[Datasets]
            SET [DisplayName] = {displayName},
                [DisplayNameArabic] = {displayNameArabic},
                [IsEnabled] = {isEnabled},
                [IsRestricted] = {isRestricted},
                [AccessMode] = {accessMode},
                [SortOrder] = {sortOrder},
                [UpdatedAt] = {updatedAt}
            WHERE [Id] = {id}
            """, cancellationToken);

        if (request.Columns is not null)
        {
            foreach (var update in request.Columns)
            {
                var column = dataset.Columns.FirstOrDefault(c => c.Id == update.Id);
                if (column is null)
                {
                    continue;
                }

                var colDisplay = string.IsNullOrWhiteSpace(update.DisplayName)
                    ? column.DisplayName
                    : update.DisplayName.Trim();
                var colDisplayArabic = OptionalArabic(update.DisplayNameArabic, column.DisplayNameArabic);
                var hidden = update.IsHidden ?? column.IsHidden;
                var filterable = update.IsFilterable ?? column.IsFilterable;
                var groupable = update.IsGroupable ?? column.IsGroupable;
                var indexed = update.IsIndexed ?? column.IsIndexed;
                var sortDirection = string.IsNullOrWhiteSpace(update.DefaultSortDirection)
                    ? column.DefaultSortDirection
                    : SortDirections.Normalize(update.DefaultSortDirection);
                var lookupId = column.LookupId;
                if (update.UpdateLookup)
                {
                    if (update.LookupId is null)
                    {
                        lookupId = null;
                    }
                    else
                    {
                        var valid = await _db.Lookups.AnyAsync(
                            item => item.Id == update.LookupId && item.IsValid,
                            cancellationToken);
                        if (!valid)
                        {
                            throw new ReportQueryException("Choose a valid lookup, or clear the lookup to keep distinct values.");
                        }

                        lookupId = update.LookupId;
                    }
                }

                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE [rb].[DatasetColumns]
                    SET [DisplayName] = {colDisplay}, [DisplayNameArabic] = {colDisplayArabic}, [IsHidden] = {hidden}, [IsFilterable] = {filterable}, [IsGroupable] = {groupable}, [IsIndexed] = {indexed}, [DefaultSortDirection] = {sortDirection}, [LookupId] = {lookupId}
                    WHERE [Id] = {update.Id}
                    """, cancellationToken);

                if (update.IsIndexed.HasValue && update.IsIndexed.Value != column.IsIndexed)
                {
                    await _cache.SyncColumnCacheIndexAsync(id, column.Id, indexed, cancellationToken);
                }
            }
        }

        var accessIds = accessMode == ReportAccessModes.People
            ? (request.AccessUserIds ?? dataset.Access.Select(row => row.UserId).ToList())
            : [];
        var accessRoleIds = accessMode == ReportAccessModes.Roles
            ? (request.AccessRoleIds ?? dataset.RoleAccess.Select(row => row.RoleId).ToList())
            : [];
        await ReplaceAccessAsync(id, accessMode, accessIds, accessRoleIds, cancellationToken);

        _db.ChangeTracker.Clear();
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.update",
                Message = $"Updated dataset {displayName}.",
                EntityType = "Dataset",
                EntityId = id,
                EntityName = displayName,
                Details = $"{{\"enabled\":{(isEnabled ? "true" : "false")},\"restricted\":{(isRestricted ? "true" : "false")}}}"
            },
            cancellationToken);
        return await Datasets().FirstAsync(d => d.Id == id, cancellationToken);
    }

    private IQueryable<Dataset> Datasets()
        => _db.Datasets
            .Include(d => d.Columns)
                .ThenInclude(c => c.Lookup)
            .Include(d => d.Access)
                .ThenInclude(row => row.User)
            .Include(d => d.RoleAccess)
                .ThenInclude(row => row.Role)
            .Include(d => d.CacheState)
            .AsSplitQuery();

    public async Task<IReadOnlyList<ReportAccessRole>> ListRolesAsync(CancellationToken cancellationToken = default)
        => await _db.Roles.AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new ReportAccessRole
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description
            })
            .ToListAsync(cancellationToken);

    private async Task ReplaceAccessAsync(
        Guid datasetId,
        string accessMode,
        IReadOnlyList<Guid>? accessUserIds,
        IReadOnlyList<Guid>? accessRoleIds,
        CancellationToken cancellationToken)
    {
        var mode = ReportAccessModes.Normalize(accessMode, accessMode != ReportAccessModes.Everyone);
        var existing = await _db.DatasetAccess
            .Where(row => row.DatasetId == datasetId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.DatasetAccess.RemoveRange(existing);
        }

        var existingRoles = await _db.DatasetRoleAccess
            .Where(row => row.DatasetId == datasetId)
            .ToListAsync(cancellationToken);
        if (existingRoles.Count > 0)
        {
            _db.DatasetRoleAccess.RemoveRange(existingRoles);
        }

        var unique = mode == ReportAccessModes.People && accessUserIds is not null
            ? accessUserIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList()
            : [];
        var roleIds = mode == ReportAccessModes.Roles && accessRoleIds is not null
            ? accessRoleIds.Where(id => id != Guid.Empty).Distinct().ToList()
            : [];

        if (unique.Count > 0)
        {
            var valid = await _db.Users.AsNoTracking()
                .Where(user => unique.Contains(user.Id) && user.IsActive)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            if (valid.Count != unique.Count)
            {
                throw new ReportQueryException("One or more selected users were not found.");
            }

            _db.DatasetAccess.AddRange(valid.Select(userId => new DatasetAccess
            {
                DatasetId = datasetId,
                UserId = userId
            }));
        }

        if (roleIds.Count > 0)
        {
            var validRoles = await _db.Roles.AsNoTracking()
                .Where(role => roleIds.Contains(role.Id))
                .Select(role => role.Id)
                .ToListAsync(cancellationToken);
            if (validRoles.Count != roleIds.Count)
            {
                throw new ReportQueryException("A selected role was not found.");
            }

            _db.DatasetRoleAccess.AddRange(validRoles.Select(roleId => new DatasetRoleAccess
            {
                DatasetId = datasetId,
                RoleId = roleId
            }));
        }

        if (existing.Count > 0 || existingRoles.Count > 0 || unique.Count > 0 || roleIds.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<DatasetSqlScript> ApplyQueryAsync(
        Dataset dataset,
        string? script,
        CancellationToken cancellationToken)
    {
        var sql = ReadOnlySelect.Normalize(script);
        var live = await _schema.ProbeQueryColumnsAsync(sql, cancellationToken);
        await GuardRemovedColumnsAsync(dataset, live, cancellationToken);
        dataset.SqlText = sql;
        dataset.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _schema.RefreshColumnsAsync(dataset, cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Datasets,
                Action = "dataset.query.update",
                Message = $"Updated the SQL for {dataset.DisplayName}.",
                EntityType = "Dataset",
                EntityId = dataset.Id,
                EntityName = dataset.DisplayName
            },
            cancellationToken);
        return new DatasetSqlScript
        {
            DatasetId = dataset.Id,
            SchemaName = dataset.SchemaName,
            ObjectName = dataset.ObjectName,
            ObjectType = dataset.ObjectType.ToString(),
            Script = sql
        };
    }

    private async Task GuardRemovedColumnsAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        var live = await _schema.ProbeQueryColumnsAsync(dataset.SqlText, cancellationToken);
        await GuardRemovedColumnsAsync(dataset, live, cancellationToken);
    }

    private async Task GuardRemovedColumnsAsync(
        Dataset dataset,
        IReadOnlyList<DiscoveredColumn> live,
        CancellationToken cancellationToken)
    {
        var existing = dataset.Columns.Count > 0
            ? dataset.Columns.ToList()
            : await _db.DatasetColumns.AsNoTracking()
                .Where(column => column.DatasetId == dataset.Id)
                .ToListAsync(cancellationToken);
        var liveNames = live.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = existing.Where(column => !liveNames.Contains(column.ColumnName)).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lookupColumns = new HashSet<Guid>();
        var idText = dataset.Id.ToString();
        var reports = await _db.ReportTemplates.AsNoTracking()
            .Where(report => report.DatasetId == dataset.Id
                || (report.DraftJson != null && report.DraftJson.Contains(idText)))
            .Select(report => new { report.DatasetId, report.LayoutJson, report.DraftJson })
            .ToListAsync(cancellationToken);
        foreach (var report in reports)
        {
            if (report.DatasetId == dataset.Id)
            {
                DatasetColumnUsage.Collect(LayoutJson.Parse(report.LayoutJson), fields, lookupColumns);
            }

            var draft = ReportDraftJson.Parse(report.DraftJson);
            if (draft is not null && draft.DatasetId == dataset.Id)
            {
                DatasetColumnUsage.Collect(draft.Layout, fields, lookupColumns);
            }
        }

        var revisions = await _db.ReportRevisions.AsNoTracking()
            .Where(revision => revision.DatasetId == dataset.Id)
            .Select(revision => revision.LayoutJson)
            .ToListAsync(cancellationToken);
        foreach (var layoutJson in revisions)
        {
            DatasetColumnUsage.Collect(LayoutJson.Parse(layoutJson), fields, lookupColumns);
        }

        var dashboards = await _db.AnalyticsDashboards.AsNoTracking()
            .Where(dashboard => dashboard.ConfigJson.Contains(idText))
            .Select(dashboard => dashboard.ConfigJson)
            .ToListAsync(cancellationToken);
        foreach (var configJson in dashboards)
        {
            DatasetColumnUsage.Collect(AnalyticsConfigJson.Deserialize(configJson), dataset.Id, fields);
        }

        var blocked = removed
            .Where(column => fields.Contains(column.ColumnName) || lookupColumns.Contains(column.Id))
            .Select(column => column.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        if (blocked.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", blocked);
        throw new ReportQueryException(
            blocked.Count == 1
                ? $"Query no longer returns column '{names}'. A report or dashboard still uses it."
                : $"Query no longer returns columns {names}. A report or dashboard still uses them.");
    }

    private static string? OptionalArabic(string? incoming, string? current)
        => incoming is null
            ? current
            : string.IsNullOrWhiteSpace(incoming)
                ? null
                : incoming.Trim();
}
