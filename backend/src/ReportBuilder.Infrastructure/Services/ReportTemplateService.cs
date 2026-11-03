using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Identity;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportTemplateService : IReportTemplateService
{
    private readonly ReportBuilderDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityLog _log;

    public ReportTemplateService(ReportBuilderDbContext db, ICurrentUser currentUser, IActivityLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _log = log;
    }

    public async Task<IReadOnlyList<ReportTemplate>> ListAsync(CancellationToken cancellationToken = default)
        => await Templates()
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(cancellationToken);

    public Task<ReportTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Templates().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<ReportTemplate> CreateAsync(ReportTemplateWriteRequest request, CancellationToken cancellationToken = default)
    {
        var dataset = await RequireEnabledDataset(request.DatasetId, cancellationToken);
        ValidateLayoutFields(dataset, request.Layout);
        var categoryId = await ResolveCategoryId(request.CategoryId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var accessMode = ReportAccessModes.Normalize(request.AccessMode, request.IsRestricted);
        var template = new ReportTemplate
        {
            Id = Guid.NewGuid(),
            Name = RequireName(request.Name),
            NameArabic = RequireArabicName(request.NameArabic),
            Description = EmptyToNull(request.Description),
            DescriptionArabic = EmptyToNull(request.DescriptionArabic),
            DatasetId = dataset.Id,
            CategoryId = categoryId,
            LayoutJson = LayoutJson.Serialize(request.Layout),
            Status = request.Status ?? ReportStatus.Draft,
            PublishedVersion = (request.Status ?? ReportStatus.Draft) == ReportStatus.Published ? 1 : 0,
            CreatedBy = _currentUser.UserId,
            IsAdminCreated = _currentUser.IsAdmin,
            IsRestricted = accessMode != ReportAccessModes.Everyone,
            AccessMode = accessMode,
            CreatedAt = now,
            UpdatedAt = now
        };

        var status = template.Status.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [rb].[ReportTemplates]
                ([Id], [Name], [NameArabic], [Description], [DescriptionArabic], [DatasetId], [CategoryId], [LayoutJson], [DraftJson], [PublishedVersion], [Status], [CreatedBy], [IsAdminCreated], [IsRestricted], [AccessMode], [CreatedAt], [UpdatedAt])
            VALUES
                ({template.Id}, {template.Name}, {template.NameArabic}, {template.Description}, {template.DescriptionArabic}, {template.DatasetId}, {template.CategoryId}, {template.LayoutJson}, {template.DraftJson}, {template.PublishedVersion}, {status}, {template.CreatedBy}, {template.IsAdminCreated}, {template.IsRestricted}, {template.AccessMode}, {template.CreatedAt}, {template.UpdatedAt})
            """, cancellationToken);

        await ReplaceAccessAsync(template.Id, template.CreatedBy, accessMode, request.AccessUserIds, request.AccessRoleIds, cancellationToken);
        if (template.Status == ReportStatus.Published)
        {
            await RecordRevisionAsync(
                template.Id,
                template.PublishedVersion,
                template.Name,
                template.NameArabic,
                template.Description,
                template.DescriptionArabic,
                template.DatasetId,
                template.CategoryId,
                template.LayoutJson,
                template.IsRestricted,
                accessMode,
                request.AccessUserIds ?? [],
                request.AccessRoleIds ?? [],
                cancellationToken);
        }
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report.create",
                Message = $"Created report {template.Name}.",
                EntityType = "Report",
                EntityId = template.Id,
                EntityName = template.Name
            },
            cancellationToken);
        return (await GetAsync(template.Id, cancellationToken))!;
    }

    public async Task<ReportTemplate?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await GetAsync(id, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var layout = LayoutJson.Parse(source.LayoutJson);
        return await CreateAsync(new ReportTemplateWriteRequest
        {
            Name = CopyName(source.Name, " copy"),
            NameArabic = CopyName(source.NameArabic, " (نسخة)"),
            Description = source.Description,
            DescriptionArabic = source.DescriptionArabic,
            DatasetId = source.DatasetId,
            Layout = layout,
            Status = ReportStatus.Draft,
            CategoryId = source.CategoryId,
            IsRestricted = source.IsRestricted,
            AccessMode = source.AccessMode,
            AccessUserIds = source.Access.Select(row => row.UserId).ToList(),
            AccessRoleIds = source.RoleAccess.Select(row => row.RoleId).ToList()
        }, cancellationToken);
    }

    private static string CopyName(string? name, string suffix)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var copy = trimmed.Length == 0 ? suffix.Trim() : trimmed + suffix;
        return copy.Length <= 200 ? copy : copy[..200];
    }

    public async Task<ReportTemplate?> UpdateAsync(Guid id, ReportTemplateWriteRequest request, CancellationToken cancellationToken = default)
    {
        var template = await Templates().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var dataset = await RequireEnabledDataset(request.DatasetId, cancellationToken);
        ValidateLayoutFields(dataset, request.Layout);
        var categoryId = await ResolveCategoryId(request.CategoryId, cancellationToken);

        var name = RequireName(request.Name);
        var nameArabic = RequireArabicName(request.NameArabic);
        var description = EmptyToNull(request.Description);
        var descriptionArabic = EmptyToNull(request.DescriptionArabic);
        var layoutJson = LayoutJson.Serialize(request.Layout);
        var requestedStatus = request.Status ?? template.Status;
        var accessMode = string.IsNullOrWhiteSpace(request.AccessMode) && request.IsRestricted is null
            ? ReportAccessModes.Normalize(template.AccessMode, template.IsRestricted)
            : ReportAccessModes.Normalize(request.AccessMode, request.IsRestricted ?? template.IsRestricted);
        var isRestricted = accessMode != ReportAccessModes.Everyone;
        var updatedAt = DateTimeOffset.UtcNow;
        var accessIds = accessMode == ReportAccessModes.People
            ? (request.AccessUserIds ?? template.Access.Select(row => row.UserId).ToArray())
            : Array.Empty<Guid>();
        var accessRoleIds = accessMode == ReportAccessModes.Roles
            ? (request.AccessRoleIds ?? template.RoleAccess.Select(row => row.RoleId).ToArray())
            : Array.Empty<Guid>();

        if (requestedStatus == ReportStatus.Draft && template.Status == ReportStatus.Published)
        {
            var draftVersion = (template.Draft?.Version ?? template.PublishedVersion) + 1;
            var draftJson = ReportDraftJson.Serialize(new ReportDraftSnapshot
            {
                Version = draftVersion,
                Name = name,
                NameArabic = nameArabic,
                Description = description,
                DescriptionArabic = descriptionArabic,
                DatasetId = dataset.Id,
                CategoryId = categoryId,
                IsRestricted = isRestricted,
                AccessMode = accessMode,
                Layout = request.Layout,
                AccessUserIds = accessIds.ToList(),
                AccessRoleIds = accessRoleIds.ToList()
            });
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportTemplates]
                SET [DraftJson] = {draftJson}, [UpdatedAt] = {updatedAt}
                WHERE [Id] = {id}
                """, cancellationToken);
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Category = ActivityCategories.Reports,
                    Action = "report.draft.save",
                    Message = $"Saved draft version {draftVersion} for published report {template.Name}.",
                    EntityType = "Report",
                    EntityId = id,
                    EntityName = template.Name,
                    Details = $"{{\"draftVersion\":{draftVersion},\"publishedVersion\":{template.PublishedVersion}}}"
                },
                cancellationToken);
            return await GetAsync(id, cancellationToken);
        }

        var status = requestedStatus.ToString();
        var publishedVersion = requestedStatus == ReportStatus.Published
            ? Math.Max(template.PublishedVersion, 0) + 1
            : template.PublishedVersion;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [rb].[ReportTemplates]
            SET [Name] = {name}, [NameArabic] = {nameArabic}, [Description] = {description}, [DescriptionArabic] = {descriptionArabic}, [DatasetId] = {dataset.Id}, [CategoryId] = {categoryId}, [LayoutJson] = {layoutJson}, [DraftJson] = NULL, [PublishedVersion] = {publishedVersion}, [Status] = {status}, [IsRestricted] = {isRestricted}, [AccessMode] = {accessMode}, [UpdatedAt] = {updatedAt}
            WHERE [Id] = {id}
            """, cancellationToken);

        await ReplaceAccessAsync(id, template.CreatedBy, accessMode, accessIds, accessRoleIds, cancellationToken);
        if (requestedStatus == ReportStatus.Published)
        {
            await RecordRevisionAsync(
                id,
                publishedVersion,
                name,
                nameArabic,
                description,
                descriptionArabic,
                dataset.Id,
                categoryId,
                layoutJson,
                isRestricted,
                accessMode,
                accessIds.ToList(),
                accessRoleIds.ToList(),
                cancellationToken);
        }
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = requestedStatus == ReportStatus.Published ? "report.publish" : "report.update",
                Message = requestedStatus == ReportStatus.Published
                    ? $"Published report {name} as version {publishedVersion}."
                    : $"Updated report {name}.",
                EntityType = "Report",
                EntityId = id,
                EntityName = name
            },
            cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ReportTemplate?> SetStatusAsync(Guid id, ReportStatus status, CancellationToken cancellationToken = default)
    {
        var template = await _db.ReportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var statusName = status.ToString();
        var updatedAt = DateTimeOffset.UtcNow;
        var draft = ReportDraftJson.Parse(template.DraftJson);
        if (status == ReportStatus.Published && draft is not null)
        {
            var dataset = await RequireEnabledDataset(draft.DatasetId, cancellationToken);
            ValidateLayoutFields(dataset, draft.Layout);
            var categoryId = await ResolveCategoryId(draft.CategoryId, cancellationToken);
            var name = RequireName(draft.Name);
            var nameArabic = RequireArabicName(draft.NameArabic);
            var description = EmptyToNull(draft.Description);
            var descriptionArabic = EmptyToNull(draft.DescriptionArabic);
            var layoutJson = LayoutJson.Serialize(draft.Layout);
            var publishedVersion = Math.Max(template.PublishedVersion, 0) + 1;
            var draftMode = ReportAccessModes.Normalize(draft.AccessMode, draft.IsRestricted);
            var draftRestricted = draftMode != ReportAccessModes.Everyone;
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportTemplates]
                SET [Name] = {name}, [NameArabic] = {nameArabic}, [Description] = {description}, [DescriptionArabic] = {descriptionArabic}, [DatasetId] = {dataset.Id}, [CategoryId] = {categoryId}, [LayoutJson] = {layoutJson}, [DraftJson] = NULL, [PublishedVersion] = {publishedVersion}, [Status] = {statusName}, [IsRestricted] = {draftRestricted}, [AccessMode] = {draftMode}, [UpdatedAt] = {updatedAt}
                WHERE [Id] = {id}
                """, cancellationToken);
            await ReplaceAccessAsync(id, template.CreatedBy, draftMode, draft.AccessUserIds, draft.AccessRoleIds, cancellationToken);
            await RecordRevisionAsync(
                id,
                publishedVersion,
                name,
                nameArabic,
                description,
                descriptionArabic,
                dataset.Id,
                categoryId,
                layoutJson,
                draftRestricted,
                draftMode,
                draft.AccessUserIds,
                draft.AccessRoleIds,
                cancellationToken);
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Category = ActivityCategories.Reports,
                    Action = "report.publish",
                    Message = $"Published draft version {draft.Version} of report {name} as version {publishedVersion}.",
                    EntityType = "Report",
                    EntityId = id,
                    EntityName = name
                },
                cancellationToken);
            return await GetAsync(id, cancellationToken);
        }

        var nextPublishedVersion = status == ReportStatus.Published && template.Status != ReportStatus.Published
            ? Math.Max(template.PublishedVersion, 0) + 1
            : template.PublishedVersion;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [rb].[ReportTemplates]
            SET [Status] = {statusName}, [PublishedVersion] = {nextPublishedVersion}, [UpdatedAt] = {updatedAt}
            WHERE [Id] = {id}
            """, cancellationToken);

        if (status == ReportStatus.Published && template.Status != ReportStatus.Published)
        {
            var accessIds = await _db.ReportAccess.AsNoTracking()
                .Where(row => row.ReportId == id)
                .Select(row => row.UserId)
                .ToListAsync(cancellationToken);
            var accessRoleIds = await _db.ReportRoleAccess.AsNoTracking()
                .Where(row => row.ReportId == id)
                .Select(row => row.RoleId)
                .ToListAsync(cancellationToken);
            var publishMode = ReportAccessModes.Normalize(template.AccessMode, template.IsRestricted);
            await RecordRevisionAsync(
                id,
                nextPublishedVersion,
                template.Name,
                template.NameArabic,
                template.Description,
                template.DescriptionArabic,
                template.DatasetId,
                template.CategoryId,
                template.LayoutJson,
                template.IsRestricted,
                publishMode,
                accessIds,
                accessRoleIds,
                cancellationToken);
        }

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = status == ReportStatus.Published ? "report.publish" : "report.unpublish",
                Message = status == ReportStatus.Published
                    ? $"Published report {template.Name}."
                    : $"Unpublished report {template.Name}.",
                EntityType = "Report",
                EntityId = id,
                EntityName = template.Name
            },
            cancellationToken);

        if (status == ReportStatus.Draft && template.Status == ReportStatus.Published)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportSchedules]
                SET [Enabled] = 0, [NextRunAt] = NULL, [UpdatedAt] = {updatedAt}
                WHERE [ReportId] = {id}
                """, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _db.ReportTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [rb].[ReportTemplates] WHERE [Id] = {id}
            """, cancellationToken);
        if (rows > 0)
        {
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Category = ActivityCategories.Reports,
                    Action = "report.delete",
                    Message = $"Deleted report {existing?.Name ?? id.ToString()}.",
                    EntityType = "Report",
                    EntityId = id,
                    EntityName = existing?.Name
                },
                cancellationToken);
        }
        return rows > 0;
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

    public Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken = default)
        => IdentityLookup.NamesAsync(_db, ids, cancellationToken);

    public async Task<ReportHistory> ListRevisionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await Templates().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return new ReportHistory();
        }

        await EnsureCurrentRevisionAsync(template, cancellationToken);
        var revisions = await _db.ReportRevisions.AsNoTracking()
            .Where(row => row.ReportId == id)
            .OrderByDescending(row => row.Version)
            .ToListAsync(cancellationToken);
        var names = await UserNamesAsync(revisions.Select(row => row.PublishedBy), cancellationToken);
        var published = LayoutJson.Parse(template.LayoutJson);
        var items = revisions.Select(row =>
        {
            var layout = LayoutJson.Parse(row.LayoutJson);
            var diff = DiffLayouts(
                layout,
                published,
                row.Name,
                template.Name,
                row.NameArabic,
                template.NameArabic);
            return new ReportRevisionItem
            {
                Version = row.Version,
                Name = row.Name,
                NameArabic = row.NameArabic,
                PublishedAt = row.PublishedAt,
                PublishedByName = row.PublishedBy is Guid publisher
                    ? names.GetValueOrDefault(publisher)
                    : null,
                IsCurrent = row.Version == template.PublishedVersion,
                OnlyHere = diff.OnlyHere,
                OnlyInPublished = diff.OnlyInPublished,
                FiltersChanged = diff.FiltersChanged,
                SortChanged = diff.SortChanged,
                NameChanged = diff.NameChanged
            };
        }).ToList();

        ReportLayoutDiff? draftDiff = null;
        if (template.Draft is { } draft && template.Status == ReportStatus.Published)
        {
            draftDiff = DiffLayouts(
                draft.Layout,
                published,
                draft.Name,
                template.Name,
                draft.NameArabic,
                template.NameArabic);
        }

        return new ReportHistory
        {
            CurrentVersion = template.PublishedVersion,
            DraftVersusPublished = draftDiff,
            Revisions = items
        };
    }

    public async Task<ReportTemplate?> RestoreRevisionAsync(Guid id, int version, CancellationToken cancellationToken = default)
    {
        var template = await Templates().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var revision = await _db.ReportRevisions.AsNoTracking()
            .FirstOrDefaultAsync(row => row.ReportId == id && row.Version == version, cancellationToken);
        var updatedAt = DateTimeOffset.UtcNow;
        if (template.Status == ReportStatus.Published && version == template.PublishedVersion)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportTemplates]
                SET [DraftJson] = NULL, [UpdatedAt] = {updatedAt}
                WHERE [Id] = {id}
                """, cancellationToken);
            await _log.WriteAsync(
                new ActivityEvent
                {
                    Category = ActivityCategories.Reports,
                    Action = "report.restore",
                    Message = $"Returned report {template.Name} to published version {version}.",
                    EntityType = "Report",
                    EntityId = id,
                    EntityName = template.Name,
                    Details = $"{{\"version\":{version},\"discardedDraft\":true}}"
                },
                cancellationToken);
            return await GetAsync(id, cancellationToken);
        }

        if (revision is null)
        {
            return null;
        }

        var layout = LayoutJson.Parse(revision.LayoutJson);
        var accessIds = ParseAccess(revision.AccessJson);
        var accessRoleIds = ParseAccess(revision.AccessRoleJson);
        var accessMode = ReportAccessModes.Normalize(revision.AccessMode, revision.IsRestricted);
        if (template.Status == ReportStatus.Published)
        {
            var draftVersion = (template.Draft?.Version ?? template.PublishedVersion) + 1;
            var draftJson = ReportDraftJson.Serialize(new ReportDraftSnapshot
            {
                Version = draftVersion,
                Name = revision.Name,
                NameArabic = revision.NameArabic,
                Description = revision.Description,
                DescriptionArabic = revision.DescriptionArabic,
                DatasetId = revision.DatasetId,
                CategoryId = revision.CategoryId,
                IsRestricted = accessMode != ReportAccessModes.Everyone,
                AccessMode = accessMode,
                Layout = layout,
                AccessUserIds = accessIds,
                AccessRoleIds = accessRoleIds
            });
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportTemplates]
                SET [DraftJson] = {draftJson}, [UpdatedAt] = {updatedAt}
                WHERE [Id] = {id}
                """, cancellationToken);
        }
        else
        {
            var layoutJson = revision.LayoutJson;
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [rb].[ReportTemplates]
                SET [Name] = {revision.Name}, [NameArabic] = {revision.NameArabic}, [Description] = {revision.Description}, [DescriptionArabic] = {revision.DescriptionArabic}, [DatasetId] = {revision.DatasetId}, [CategoryId] = {revision.CategoryId}, [LayoutJson] = {layoutJson}, [DraftJson] = NULL, [IsRestricted] = {accessMode != ReportAccessModes.Everyone}, [AccessMode] = {accessMode}, [UpdatedAt] = {updatedAt}
                WHERE [Id] = {id}
                """, cancellationToken);
            await ReplaceAccessAsync(id, template.CreatedBy, accessMode, accessIds, accessRoleIds, cancellationToken);
        }

        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report.restore",
                Message = $"Restored version {version} of report {template.Name}.",
                EntityType = "Report",
                EntityId = id,
                EntityName = template.Name,
                Details = $"{{\"version\":{version}}}"
            },
            cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task EnsureCurrentRevisionAsync(ReportTemplate template, CancellationToken cancellationToken)
    {
        if (template.Status != ReportStatus.Published || template.PublishedVersion <= 0)
        {
            return;
        }

        var exists = await _db.ReportRevisions.AsNoTracking()
            .AnyAsync(row => row.ReportId == template.Id, cancellationToken);
        if (exists)
        {
            return;
        }

        await RecordRevisionAsync(
            template.Id,
            template.PublishedVersion,
            template.Name,
            template.NameArabic,
            template.Description,
            template.DescriptionArabic,
            template.DatasetId,
            template.CategoryId,
            template.LayoutJson,
            template.IsRestricted,
            ReportAccessModes.Normalize(template.AccessMode, template.IsRestricted),
            template.Access.Select(row => row.UserId).ToList(),
            template.RoleAccess.Select(row => row.RoleId).ToList(),
            cancellationToken,
            template.UpdatedAt,
            template.CreatedBy);
    }

    private async Task RecordRevisionAsync(
        Guid reportId,
        int version,
        string name,
        string? nameArabic,
        string? description,
        string? descriptionArabic,
        Guid datasetId,
        Guid? categoryId,
        string layoutJson,
        bool isRestricted,
        string accessMode,
        IReadOnlyList<Guid> accessUserIds,
        IReadOnlyList<Guid> accessRoleIds,
        CancellationToken cancellationToken,
        DateTimeOffset? publishedAt = null,
        Guid? publishedBy = null)
    {
        if (version <= 0)
        {
            return;
        }

        var id = Guid.NewGuid();
        var when = publishedAt ?? DateTimeOffset.UtcNow;
        var who = publishedBy ?? _currentUser.UserId;
        var accessJson = JsonSerializer.Serialize(accessUserIds);
        var accessRoleJson = JsonSerializer.Serialize(accessRoleIds);
        var storedMode = ReportAccessModes.Normalize(accessMode, isRestricted);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (SELECT 1 FROM [rb].[ReportRevisions] WHERE [ReportId] = {reportId} AND [Version] = {version})
            INSERT INTO [rb].[ReportRevisions]
                ([Id], [ReportId], [Version], [Name], [NameArabic], [Description], [DescriptionArabic], [DatasetId], [CategoryId], [LayoutJson], [IsRestricted], [AccessMode], [AccessJson], [AccessRoleJson], [PublishedBy], [PublishedAt])
            VALUES
                ({id}, {reportId}, {version}, {name}, {nameArabic}, {description}, {descriptionArabic}, {datasetId}, {categoryId}, {layoutJson}, {isRestricted}, {storedMode}, {accessJson}, {accessRoleJson}, {who}, {when})
            """, cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [rb].[ReportRevisions]
            WHERE [ReportId] = {reportId}
              AND [Version] NOT IN (
                SELECT [Version] FROM (
                  SELECT TOP (30) [Version]
                  FROM [rb].[ReportRevisions]
                  WHERE [ReportId] = {reportId}
                  ORDER BY [Version] DESC
                ) AS [Keep])
            """, cancellationToken);
    }

    private static List<Guid> ParseAccess(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
    }

    private static ReportLayoutDiff DiffLayouts(
        ReportLayout subject,
        ReportLayout published,
        string? subjectName,
        string? publishedName,
        string? subjectNameArabic,
        string? publishedNameArabic)
    {
        var subjectColumns = subject.Columns ?? [];
        var publishedColumns = published.Columns ?? [];
        var subjectKeys = subjectColumns.Select(ColumnKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var publishedKeys = publishedColumns.Select(ColumnKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ReportLayoutDiff
        {
            OnlyHere = subjectColumns
                .Where(column => !publishedKeys.Contains(ColumnKey(column)))
                .Select(ColumnLabel)
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OnlyInPublished = publishedColumns
                .Where(column => !subjectKeys.Contains(ColumnKey(column)))
                .Select(ColumnLabel)
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FiltersChanged = !SameFilters(subject, published),
            SortChanged = !SameSort(subject, published),
            NameChanged = !string.Equals(subjectName?.Trim(), publishedName?.Trim(), StringComparison.Ordinal)
                || !string.Equals(subjectNameArabic?.Trim(), publishedNameArabic?.Trim(), StringComparison.Ordinal)
        };
    }

    private static string ColumnKey(LayoutColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.Field))
        {
            return column.Field.Trim();
        }

        if (!string.IsNullOrWhiteSpace(column.Expression))
        {
            return "calc:" + column.Expression.Trim();
        }

        return column.Alias?.Trim() ?? string.Empty;
    }

    private static string ColumnLabel(LayoutColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.Alias))
        {
            return column.Alias.Trim();
        }

        if (!string.IsNullOrWhiteSpace(column.Field))
        {
            return column.Field.Trim();
        }

        return column.Expression?.Trim() ?? string.Empty;
    }

    private static bool SameFilters(ReportLayout left, ReportLayout right)
    {
        var a = left.Filters ?? [];
        var b = right.Filters ?? [];
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Field, b[i].Field, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Operator, b[i].Operator, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal)
                || a[i].IsFixed != b[i].IsFixed
                || a[i].IsHidden != b[i].IsHidden)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSort(ReportLayout left, ReportLayout right)
    {
        var a = left.OrderBy ?? [];
        var b = right.OrderBy ?? [];
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Field, b[i].Field, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Direction, b[i].Direction, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private IQueryable<ReportTemplate> Templates()
        => _db.ReportTemplates.AsNoTracking()
            .Include(t => t.Dataset)
                .ThenInclude(d => d.CacheState)
            .Include(t => t.Category)
            .Include(t => t.Access)
                .ThenInclude(row => row.User)
            .Include(t => t.RoleAccess)
                .ThenInclude(row => row.Role);

    private async Task ReplaceAccessAsync(
        Guid reportId,
        Guid? ownerId,
        string accessMode,
        IReadOnlyList<Guid>? accessUserIds,
        IReadOnlyList<Guid>? accessRoleIds,
        CancellationToken cancellationToken)
    {
        var mode = ReportAccessModes.Normalize(accessMode, accessMode != ReportAccessModes.Everyone);
        var existingUsers = await _db.ReportAccess
            .Where(row => row.ReportId == reportId)
            .ToListAsync(cancellationToken);
        var existingRoles = await _db.ReportRoleAccess
            .Where(row => row.ReportId == reportId)
            .ToListAsync(cancellationToken);
        if (existingUsers.Count > 0)
        {
            _db.ReportAccess.RemoveRange(existingUsers);
        }

        if (existingRoles.Count > 0)
        {
            _db.ReportRoleAccess.RemoveRange(existingRoles);
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

            _db.ReportAccess.AddRange(valid.Select(userId => new ReportAccess
            {
                ReportId = reportId,
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

            _db.ReportRoleAccess.AddRange(validRoles.Select(roleId => new ReportRoleAccess
            {
                ReportId = reportId,
                RoleId = roleId
            }));
        }

        if (existingUsers.Count > 0 || existingRoles.Count > 0 || userIds.Count > 0 || roleIds.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

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

    private async Task<Dataset> RequireEnabledDataset(Guid datasetId, CancellationToken cancellationToken)
    {
        var dataset = await _db.Datasets
            .Include(d => d.Columns).ThenInclude(c => c.Lookup)
            .Include(d => d.Access)
            .Include(d => d.RoleAccess)
                .ThenInclude(row => row.Role)
            .FirstOrDefaultAsync(d => d.Id == datasetId, cancellationToken)
            ?? throw new ReportQueryException("Dataset was not found.");

        if (!dataset.IsEnabled)
        {
            throw new ReportQueryException("Dataset is not enabled.");
        }

        if (!DatasetAccessRules.CanUseInDesigner(dataset, _currentUser))
        {
            throw new ReportQueryException("You are not allowed to use this dataset in the designer.");
        }

        return dataset;
    }

    private async Task<Guid?> ResolveCategoryId(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null || categoryId == Guid.Empty)
        {
            return null;
        }

        var exists = await _db.ReportCategories.AsNoTracking()
            .AnyAsync(category => category.Id == categoryId, cancellationToken);
        if (!exists)
        {
            throw new ReportQueryException("Report category was not found.");
        }

        return categoryId;
    }

    private const int MinReportNameLength = 5;

    private static string RequireName(string? name)
        => RequireReportName(
            name,
            "Report name is required.",
            "Report name must be at least 5 characters.");

    private static string RequireArabicName(string? name)
        => RequireReportName(
            name,
            "Arabic report name is required.",
            "Arabic report name must be at least 5 characters.");

    private static string RequireReportName(string? name, string requiredMessage, string tooShortMessage)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ReportQueryException(requiredMessage);
        }

        if (trimmed.Length < MinReportNameLength)
        {
            throw new ReportQueryException(tooShortMessage);
        }

        return trimmed;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly HashSet<string> ChartTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bar", "line", "area", "pie", "doughnut"
    };

    private static void ValidateLayoutFields(Dataset dataset, ReportLayout layout)
    {
        if (layout.IsChart)
        {
            layout.Kind = "chart";
            var type = layout.Chart?.Type?.Trim();
            if (string.IsNullOrWhiteSpace(type) || !ChartTypes.Contains(type))
            {
                throw new ReportQueryException("Choose a chart type.");
            }

            if (layout.GroupBy.Count == 0 || string.IsNullOrWhiteSpace(layout.GroupBy[0]))
            {
                throw new ReportQueryException("Choose a category field for the chart.");
            }

            if (!layout.Columns.Any(column => !string.IsNullOrWhiteSpace(column.Aggregate)))
            {
                throw new ReportQueryException("Add at least one chart measure.");
            }
        }
        else if (layout.Columns.Count == 0)
        {
            throw new ReportQueryException("Select at least one report column.");
        }

        var allowed = dataset.Columns
            .Where(c => !c.IsHidden)
            .Select(c => c.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var column in layout.Columns)
        {
            if (!string.IsNullOrWhiteSpace(column.Expression))
            {
                if (string.IsNullOrWhiteSpace(column.Alias))
                {
                    throw new ReportQueryException("A calculated column needs a label.");
                }

                AnalyticsExpression.Validate(column.Expression, dataset, layout.GroupBy.Count > 0);
                continue;
            }

            var field = column.Field;
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            if (!allowed.Contains(field) && !LookupColumnPlan.IsLookupColumn(dataset, field))
            {
                throw new ReportQueryException($"Unknown or hidden field '{field}'.");
            }
        }

        foreach (var field in layout.Filters.Select(filter => filter.Field))
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            if (!allowed.Contains(field) && !LookupColumnPlan.IsLookupColumn(dataset, field))
            {
                throw new ReportQueryException($"Unknown or hidden field '{field}'.");
            }
        }

        foreach (var field in layout.GroupBy.Concat(layout.OrderBy.Select(order => order.Field)))
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            if (!allowed.Contains(field))
            {
                throw new ReportQueryException($"Unknown or hidden field '{field}'.");
            }
        }

        ValidateUserScope(dataset, layout);
        ValidateUrlTemplates(layout);
        ValidateFilterCascades(layout);
        ValidateConditionalFormat(layout);
    }

    private static void ValidateConditionalFormat(ReportLayout layout)
    {
        foreach (var column in layout.Columns)
        {
            column.ConditionalFormat = ConditionalFormats.Sanitize(column.ConditionalFormat);
        }
    }

    private static void ValidateFilterCascades(ReportLayout layout)
    {
        var fields = layout.Filters
            .Select(filter => filter.Field)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filter in layout.Filters)
        {
            if (string.IsNullOrWhiteSpace(filter.DependsOn))
            {
                filter.DependsOn = null;
                continue;
            }

            var parent = filter.DependsOn.Trim();
            if (!fields.Contains(parent))
            {
                throw new ReportQueryException(
                    $"Filter '{filter.Field}' depends on '{parent}', which is not a filter on this report.");
            }

            if (parent.Equals(filter.Field, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportQueryException($"Filter '{filter.Field}' cannot depend on itself.");
            }

            filter.DependsOn = layout.Filters
                .First(item => item.Field.Equals(parent, StringComparison.OrdinalIgnoreCase))
                .Field;
        }

        foreach (var filter in layout.Filters)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = filter.DependsOn;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (current.Equals(filter.Field, StringComparison.OrdinalIgnoreCase) || !seen.Add(current))
                {
                    throw new ReportQueryException("Filter Depends on cannot form a cycle.");
                }

                current = layout.Filters
                    .FirstOrDefault(item => item.Field.Equals(current, StringComparison.OrdinalIgnoreCase))
                    ?.DependsOn;
            }
        }
    }

    private static void ValidateUrlTemplates(ReportLayout layout)
    {
        foreach (var column in layout.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.UrlTemplate))
            {
                column.UrlTemplate = null;
                continue;
            }

            var trimmed = column.UrlTemplate.Trim();
            if (trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("data:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportQueryException("Row action URLs cannot use script protocols.");
            }

            column.UrlTemplate = trimmed;
        }
    }

    private static void ValidateUserScope(Dataset dataset, ReportLayout layout)
    {
        if (layout.UserScope?.Enabled != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(layout.UserScope.Field))
        {
            throw new ReportQueryException("Choose a dataset column for current-user scoping.");
        }

        var meta = dataset.Columns.FirstOrDefault(column =>
            column.ColumnName.Equals(layout.UserScope.Field.Trim(), StringComparison.OrdinalIgnoreCase));

        if (meta is null || meta.IsHidden)
        {
            throw new ReportQueryException($"Unknown or hidden field '{layout.UserScope.Field}'.");
        }

        if (!meta.IsFilterable)
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used for user scoping.");
        }

        if (!UserScopeProperties.IsCompatible(
                UserScopeProperties.Normalize(layout.UserScope.Property),
                meta.SqlType))
        {
            throw new ReportQueryException(
                $"The selected user property cannot be matched against column '{meta.ColumnName}' ({meta.SqlType}).");
        }
    }
}
