using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class AnalyticsDashboardService : IAnalyticsDashboardService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public AnalyticsDashboardService(
        ReportBuilderDbContext db,
        ICurrentUser currentUser,
        IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<IReadOnlyList<AnalyticsDashboard>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await Dashboards()
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
        return items.Where(item => AnalyticsAccessRules.CanView(item, _currentUser)).ToList();
    }

    public async Task<AnalyticsDashboard?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await Dashboards().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (item is null || !AnalyticsAccessRules.CanView(item, _currentUser))
        {
            return null;
        }

        return item;
    }

    public async Task<AnalyticsDashboard> CreateAsync(
        AnalyticsDashboardWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateConfigAsync(request.Config, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var accessMode = ReportAccessModes.Normalize(request.AccessMode, request.IsRestricted);
        var entity = new AnalyticsDashboard
        {
            Id = Guid.NewGuid(),
            Name = RequireName(request.Name),
            NameArabic = EmptyToNull(request.NameArabic),
            Description = EmptyToNull(request.Description),
            DescriptionArabic = EmptyToNull(request.DescriptionArabic),
            ConfigJson = AnalyticsConfigJson.Serialize(NormalizeConfig(request.Config)),
            Status = request.Status ?? ReportStatus.Draft,
            IsRestricted = accessMode != ReportAccessModes.Everyone,
            AccessMode = accessMode,
            CreatedBy = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AnalyticsDashboards.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await ReplaceAccessAsync(entity.Id, entity.CreatedBy, accessMode, request.AccessUserIds, request.AccessRoleIds, cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Analytics,
                Action = "analytics.create",
                Message = $"Created analytics dashboard {entity.Name}.",
                EntityType = "AnalyticsDashboard",
                EntityId = entity.Id,
                EntityName = entity.Name
            },
            cancellationToken);

        return (await GetAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AnalyticsDashboard?> UpdateAsync(
        Guid id,
        AnalyticsDashboardWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.AnalyticsDashboards
            .Include(item => item.Access)
            .Include(item => item.RoleAccess)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!AnalyticsAccessRules.CanDesign(entity, _currentUser))
        {
            throw new IdentityException("Only the dashboard owner or an administrator can change this dashboard.", 403);
        }

        await ValidateConfigAsync(request.Config, cancellationToken);

        entity.Name = RequireName(request.Name);
        entity.NameArabic = EmptyToNull(request.NameArabic);
        entity.Description = EmptyToNull(request.Description);
        entity.DescriptionArabic = EmptyToNull(request.DescriptionArabic);
        entity.ConfigJson = AnalyticsConfigJson.Serialize(NormalizeConfig(request.Config));
        entity.Status = request.Status ?? entity.Status;
        var accessMode = string.IsNullOrWhiteSpace(request.AccessMode) && request.IsRestricted is null
            ? ReportAccessModes.Normalize(entity.AccessMode, entity.IsRestricted)
            : ReportAccessModes.Normalize(request.AccessMode, request.IsRestricted ?? entity.IsRestricted);
        entity.IsRestricted = accessMode != ReportAccessModes.Everyone;
        entity.AccessMode = accessMode;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var accessIds = accessMode == ReportAccessModes.People
            ? (request.AccessUserIds ?? entity.Access.Select(row => row.UserId).ToArray())
            : Array.Empty<Guid>();
        var accessRoleIds = accessMode == ReportAccessModes.Roles
            ? (request.AccessRoleIds ?? entity.RoleAccess.Select(row => row.RoleId).ToArray())
            : Array.Empty<Guid>();
        await ReplaceAccessAsync(id, entity.CreatedBy, accessMode, accessIds, accessRoleIds, cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Analytics,
                Action = "analytics.update",
                Message = $"Updated analytics dashboard {entity.Name}.",
                EntityType = "AnalyticsDashboard",
                EntityId = entity.Id,
                EntityName = entity.Name
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.AnalyticsDashboards
            .Include(item => item.Access)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        if (!AnalyticsAccessRules.CanDesign(entity, _currentUser))
        {
            throw new IdentityException("Only the dashboard owner or an administrator can delete this dashboard.", 403);
        }

        var name = entity.Name;
        _db.AnalyticsDashboards.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Analytics,
                Action = "analytics.delete",
                Message = $"Deleted analytics dashboard {name}.",
                EntityType = "AnalyticsDashboard",
                EntityId = id,
                EntityName = name
            },
            cancellationToken);
        return true;
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

    private IQueryable<AnalyticsDashboard> Dashboards()
        => _db.AnalyticsDashboards.AsNoTracking()
            .Include(item => item.Access)
                .ThenInclude(row => row.User)
            .Include(item => item.RoleAccess)
                .ThenInclude(row => row.Role);

    private async Task ReplaceAccessAsync(
        Guid dashboardId,
        Guid? ownerId,
        string accessMode,
        IReadOnlyList<Guid>? accessUserIds,
        IReadOnlyList<Guid>? accessRoleIds,
        CancellationToken cancellationToken)
    {
        var mode = ReportAccessModes.Normalize(accessMode, accessMode != ReportAccessModes.Everyone);
        var existingUsers = await _db.AnalyticsAccess
            .Where(row => row.DashboardId == dashboardId)
            .ToListAsync(cancellationToken);
        var existingRoles = await _db.AnalyticsRoleAccess
            .Where(row => row.DashboardId == dashboardId)
            .ToListAsync(cancellationToken);
        if (existingUsers.Count > 0)
        {
            _db.AnalyticsAccess.RemoveRange(existingUsers);
        }

        if (existingRoles.Count > 0)
        {
            _db.AnalyticsRoleAccess.RemoveRange(existingRoles);
        }

        var userIds = mode == ReportAccessModes.People && accessUserIds is not null
            ? accessUserIds.Where(id => id != Guid.Empty && id != ownerId).Distinct().ToList()
            : [];
        var roleIds = mode == ReportAccessModes.Roles && accessRoleIds is not null
            ? accessRoleIds.Where(id => id != Guid.Empty).Distinct().ToList()
            : [];

        if (userIds.Count > 0)
        {
            var valid = await _db.Users.AsNoTracking()
                .Where(user => userIds.Contains(user.Id) && user.IsActive)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            if (valid.Count != userIds.Count)
            {
                throw new ReportQueryException("One or more selected users were not found.");
            }

            foreach (var userId in valid)
            {
                _db.AnalyticsAccess.Add(new AnalyticsAccess
                {
                    DashboardId = dashboardId,
                    UserId = userId
                });
            }
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

            foreach (var roleId in validRoles)
            {
                _db.AnalyticsRoleAccess.Add(new AnalyticsRoleAccess
                {
                    DashboardId = dashboardId,
                    RoleId = roleId
                });
            }
        }

        if (existingUsers.Count > 0 || existingRoles.Count > 0 || userIds.Count > 0 || roleIds.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ValidateConfigAsync(AnalyticsDashboardConfig config, CancellationToken cancellationToken)
    {
        if (config.Sources.Count == 0)
        {
            throw new IdentityException("Add at least one catalog dataset as a source.");
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in config.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                throw new IdentityException("Each source needs an id.");
            }

            if (source.DatasetId == Guid.Empty)
            {
                throw new IdentityException("Each source must reference a catalog dataset.");
            }

            if (!aliases.Add(source.Id))
            {
                throw new IdentityException($"Duplicate source id '{source.Id}'.");
            }
        }

        var datasetIds = config.Sources.Select(item => item.DatasetId).Distinct().ToList();
        var enabled = await _db.Datasets
            .AsNoTracking()
            .Where(dataset => datasetIds.Contains(dataset.Id) && dataset.IsEnabled)
            .Select(dataset => dataset.Id)
            .ToListAsync(cancellationToken);

        var missing = datasetIds.Except(enabled).ToList();
        if (missing.Count > 0)
        {
            throw new IdentityException("One or more sources reference datasets that are missing or disabled.");
        }

        foreach (var source in config.Sources)
        {
            var dataset = await _db.Datasets
                .AsNoTracking()
                .Include(item => item.Access)
                .Include(item => item.RoleAccess)
                    .ThenInclude(row => row.Role)
                .FirstAsync(item => item.Id == source.DatasetId, cancellationToken);
            if (!DatasetAccessRules.CanUseInDesigner(dataset, _currentUser))
            {
                throw new IdentityException($"You do not have access to dataset '{dataset.DisplayName}'.", 403);
            }
        }

        var sourceIds = config.Sources.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var datasetsWithColumns = await _db.Datasets
            .AsNoTracking()
            .Include(item => item.Columns)
            .Where(item => datasetIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var link in config.Links)
        {
            if (!sourceIds.Contains(link.LeftSourceId) || !sourceIds.Contains(link.RightSourceId))
            {
                throw new IdentityException("Every link must reference valid source ids.");
            }

            if (link.LeftKeys.Count == 0 || link.LeftKeys.Count != link.RightKeys.Count)
            {
                throw new IdentityException("Link keys must be non-empty and paired left/right.");
            }

            if (link.LeftKeys.Any(string.IsNullOrWhiteSpace) || link.RightKeys.Any(string.IsNullOrWhiteSpace))
            {
                throw new IdentityException("Every join key field must be selected.");
            }

            var leftSource = config.Sources.First(item => item.Id.Equals(link.LeftSourceId, StringComparison.OrdinalIgnoreCase));
            var rightSource = config.Sources.First(item => item.Id.Equals(link.RightSourceId, StringComparison.OrdinalIgnoreCase));
            var leftDataset = datasetsWithColumns[leftSource.DatasetId];
            var rightDataset = datasetsWithColumns[rightSource.DatasetId];
            for (var i = 0; i < link.LeftKeys.Count; i++)
            {
                RequireCatalogColumn(leftDataset, link.LeftKeys[i], "link left key");
                RequireCatalogColumn(rightDataset, link.RightKeys[i], "link right key");
            }
        }

        foreach (var dimension in config.Dimensions)
        {
            if (!sourceIds.Contains(dimension.SourceId))
            {
                throw new IdentityException("Each dimension needs a valid source.");
            }

            var source = config.Sources.First(item => item.Id.Equals(dimension.SourceId, StringComparison.OrdinalIgnoreCase));
            var dataset = datasetsWithColumns[source.DatasetId];
            if (!string.IsNullOrWhiteSpace(dimension.Expression))
            {
                try
                {
                    AnalyticsExpression.Validate(dimension.Expression, dataset, allowAggregates: false);
                }
                catch (ReportQueryException ex)
                {
                    throw new IdentityException($"Dimension '{dimension.Label}': {ex.Message}");
                }
            }
            else if (string.IsNullOrWhiteSpace(dimension.Field))
            {
                throw new IdentityException("Each dimension needs a field or expression.");
            }
            else
            {
                RequireCatalogColumn(dataset, dimension.Field, $"dimension '{dimension.Label}'");
            }
        }

        foreach (var measure in config.Measures)
        {
            if (!sourceIds.Contains(measure.SourceId))
            {
                throw new IdentityException("Each measure needs a valid source.");
            }

            var aggregation = measure.Aggregation.Trim().ToLowerInvariant();
            if (aggregation is not ("count" or "sum" or "avg" or "min" or "max" or "expr" or "expression" or "calc"))
            {
                throw new IdentityException("Measure aggregation must be count, sum, avg, min, max, or expr.");
            }

            var source = config.Sources.First(item => item.Id.Equals(measure.SourceId, StringComparison.OrdinalIgnoreCase));
            var dataset = datasetsWithColumns[source.DatasetId];

            if (aggregation is "expr" or "expression" or "calc")
            {
                if (string.IsNullOrWhiteSpace(measure.Expression))
                {
                    throw new IdentityException($"Measure '{measure.Label}' needs a custom expression.");
                }

                try
                {
                    AnalyticsExpression.Validate(measure.Expression, dataset, allowAggregates: true);
                }
                catch (ReportQueryException ex)
                {
                    throw new IdentityException($"Measure '{measure.Label}': {ex.Message}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(measure.Expression))
            {
                try
                {
                    AnalyticsExpression.Validate(measure.Expression, dataset, allowAggregates: false);
                }
                catch (ReportQueryException ex)
                {
                    throw new IdentityException($"Measure '{measure.Label}': {ex.Message}");
                }
            }
            else if (aggregation != "count" && string.IsNullOrWhiteSpace(measure.Field))
            {
                throw new IdentityException("Non-count measures require a field or expression.");
            }
            else if (!string.IsNullOrWhiteSpace(measure.Field))
            {
                RequireCatalogColumn(dataset, measure.Field, $"measure '{measure.Label}'");
            }
        }

        foreach (var column in config.DetailColumns)
        {
            if (!sourceIds.Contains(column.SourceId) || string.IsNullOrWhiteSpace(column.Field))
            {
                throw new IdentityException("Each detail column needs a source and field.");
            }

            var source = config.Sources.First(item => item.Id.Equals(column.SourceId, StringComparison.OrdinalIgnoreCase));
            RequireCatalogColumn(datasetsWithColumns[source.DatasetId], column.Field, $"detail column '{column.Label}'");
            if (!string.IsNullOrWhiteSpace(column.UrlTemplate))
            {
                ValidateUrlTemplate(column.UrlTemplate);
            }
        }

        var dimensionIds = config.Dimensions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var measureIds = config.Measures.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var chart in config.Charts)
        {
            if (!dimensionIds.Contains(chart.DimensionId))
            {
                throw new IdentityException($"Chart '{chart.Title}' needs a valid dimension.");
            }

            if (!measureIds.Contains(chart.MeasureId))
            {
                throw new IdentityException($"Chart '{chart.Title}' needs a valid measure.");
            }

            var type = chart.Type.Trim().ToLowerInvariant();
            if (type is not ("bar" or "line" or "pie"))
            {
                throw new IdentityException("Chart type must be bar, line, or pie.");
            }

            var state = chart.State.Trim().ToLowerInvariant();
            if (state is not ("default" or "compare" or ""))
            {
                throw new IdentityException("Chart state must be default or compare.");
            }
        }

        if (config.Map is { Enabled: true } map)
        {
            if (!sourceIds.Contains(map.SourceId))
            {
                throw new IdentityException("Map needs a valid source.");
            }

            var source = config.Sources.First(item => item.Id.Equals(map.SourceId, StringComparison.OrdinalIgnoreCase));
            var dataset = datasetsWithColumns[source.DatasetId];
            RequireCatalogColumn(dataset, map.LatField, "map latitude");
            RequireCatalogColumn(dataset, map.LngField, "map longitude");
            if (!string.IsNullOrWhiteSpace(map.LabelField))
            {
                RequireCatalogColumn(dataset, map.LabelField, "map label");
            }
        }
    }

    private static void ValidateUrlTemplate(string template)
    {
        var trimmed = template.Trim();
        if (trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("data:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new IdentityException("Row action URLs cannot use script protocols.");
        }
    }

    private static void RequireCatalogColumn(Dataset dataset, string field, string context)
    {
        var exists = dataset.Columns.Any(column =>
            !column.IsHidden
            && column.ColumnName.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            throw new IdentityException(
                $"Unknown column '{field}' for {context} on {dataset.SchemaName}.{dataset.ObjectName}.");
        }
    }

    private static AnalyticsDashboardConfig NormalizeConfig(AnalyticsDashboardConfig config)
        => new()
        {
            Version = config.Version <= 0 ? 1 : config.Version,
            Sources = config.Sources,
            Links = config.Links,
            Dimensions = config.Dimensions,
            Measures = config.Measures,
            DetailColumns = config.DetailColumns,
            Charts = config.Charts,
            Map = config.Map
        };

    private static string RequireName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 2 or > 200)
        {
            throw new IdentityException("Dashboard name must be 2–200 characters.");
        }

        return value;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
