using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Api.Contracts;

public sealed class DatasetDto
{
    public Guid Id { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public bool IsCached { get; set; }
    public string CacheScheduleMode { get; set; } = CacheScheduleModes.Manual;
    public int? CacheIntervalMinutes { get; set; }
    public string? CacheDailyAtLocalTime { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<Guid> AccessUserIds { get; set; } = [];
    public List<ReportPersonDto> AccessUsers { get; set; } = [];
    public List<Guid> AccessRoleIds { get; set; } = [];
    public List<ReportAccessRoleDto> AccessRoles { get; set; } = [];
    public List<DatasetColumnDto> Columns { get; set; } = [];
    public DatasetCacheStateDto? Cache { get; set; }

    public static DatasetDto From(Dataset dataset) => new()
    {
        Id = dataset.Id,
        SchemaName = dataset.SchemaName,
        ObjectName = dataset.ObjectName,
        ObjectType = dataset.ObjectType.ToString(),
        DisplayName = dataset.DisplayName,
        DisplayNameArabic = dataset.DisplayNameArabic,
        IsEnabled = dataset.IsEnabled,
        IsRestricted = dataset.IsRestricted,
        AccessMode = ReportAccessModes.Normalize(dataset.AccessMode, dataset.IsRestricted),
        IsCached = dataset.IsCached,
        CacheScheduleMode = CacheScheduleModes.Normalize(dataset.CacheScheduleMode),
        CacheIntervalMinutes = dataset.CacheIntervalMinutes,
        CacheDailyAtLocalTime = dataset.CacheDailyAtLocalTime,
        SortOrder = dataset.SortOrder,
        UpdatedAt = dataset.UpdatedAt,
        AccessUserIds = dataset.Access.Select(row => row.UserId).ToList(),
        AccessUsers = dataset.Access.Select(ReportPersonDto.FromDatasetAccess).ToList(),
        AccessRoleIds = dataset.RoleAccess.Select(row => row.RoleId).ToList(),
        AccessRoles = dataset.RoleAccess.Select(ReportAccessRoleDto.From).ToList(),
        Columns = dataset.Columns
            .OrderBy(c => c.Ordinal)
            .Select(DatasetColumnDto.From)
            .ToList(),
        Cache = DatasetCacheStateDto.From(dataset)
    };
}

public sealed class DatasetCacheStateDto
{
    public string Status { get; set; } = DatasetCacheStatuses.None;
    public string? CacheTableName { get; set; }
    public DateTimeOffset? LastRefreshStartedAt { get; set; }
    public DateTimeOffset? LastRefreshCompletedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? NextRefreshAt { get; set; }
    public long? RowCount { get; set; }
    public int? DurationMs { get; set; }
    public long? SizeBytes { get; set; }
    public string? RefreshedByServer { get; set; }
    public Guid? RefreshedByUserId { get; set; }
    public string? LastError { get; set; }
    public int CacheVersion { get; set; }
    public string? CacheProvider { get; set; }
    public string ServingFrom { get; set; } = "live";

    public static DatasetCacheStateDto? From(Dataset dataset)
    {
        var state = dataset.CacheState;
        if (state is null && !dataset.IsCached)
        {
            return null;
        }

        var ready = DatasetCacheRouting.IsReady(dataset);
        var provider = ready ? DatasetCacheRouting.ProviderOf(dataset) : CacheProviders.Normalize(state?.CacheProvider);

        return new DatasetCacheStateDto
        {
            Status = state?.Status ?? DatasetCacheStatuses.None,
            CacheTableName = state?.CacheTableName,
            LastRefreshStartedAt = state?.LastRefreshStartedAt,
            LastRefreshCompletedAt = state?.LastRefreshCompletedAt,
            LastSuccessAt = state?.LastSuccessAt,
            NextRefreshAt = state?.NextRefreshAt,
            RowCount = state?.RowCount,
            DurationMs = state?.DurationMs,
            SizeBytes = state?.SizeBytes,
            RefreshedByServer = state?.RefreshedByServer,
            RefreshedByUserId = state?.RefreshedByUserId,
            LastError = state?.LastError,
            CacheVersion = state?.CacheVersion ?? 0,
            CacheProvider = provider,
            ServingFrom = ready ? "cache" : "live"
        };
    }
}

public sealed class LookupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string? SqlText { get; set; }
    public string KeyColumn { get; set; } = string.Empty;
    public string LabelColumn { get; set; } = string.Empty;
    public string? SortColumn { get; set; }
    public string SortDirection { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }
    public List<LookupColumnDto> Columns { get; set; } = [];

    public static LookupDto From(Lookup lookup)
    {
        List<LookupColumnDto> columns = [];
        try
        {
            columns = System.Text.Json.JsonSerializer.Deserialize<List<LookupColumnDto>>(
                lookup.ColumnsJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            columns = [];
        }

        return new LookupDto
        {
            Id = lookup.Id,
            Name = lookup.Name,
            NameArabic = lookup.NameArabic,
            SourceKind = lookup.SourceKind.ToString(),
            SchemaName = lookup.SchemaName,
            ObjectName = lookup.ObjectName,
            SqlText = lookup.SqlText,
            KeyColumn = lookup.KeyColumn,
            LabelColumn = lookup.LabelColumn,
            SortColumn = lookup.SortColumn,
            SortDirection = SortDirections.Normalize(lookup.SortDirection, SortDirections.Asc),
            IsValid = lookup.IsValid,
            ValidationError = lookup.ValidationError,
            Columns = columns
        };
    }
}

public sealed class LookupObjectDto
{
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
}

public sealed class LookupQueryPreviewRequest
{
    public string? SqlText { get; set; }
}

public sealed class LookupColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DisplayNameArabic { get; set; }
    public string? Locale { get; set; }
    public bool? Included { get; set; }
    public bool? ShowInDesigner { get; set; }
}

public sealed class DatasetColumnDto
{
    public Guid Id { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }
    public bool IsHidden { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsGroupable { get; set; }
    public bool IsIndexed { get; set; }
    public string DefaultSortDirection { get; set; } = string.Empty;
    public Guid? LookupId { get; set; }
    public string? LookupName { get; set; }
    public string? LookupNameArabic { get; set; }
    public List<LookupColumnDto> LookupColumns { get; set; } = [];
    public int Ordinal { get; set; }

    public static DatasetColumnDto From(DatasetColumn column) => new()
    {
        Id = column.Id,
        ColumnName = column.ColumnName,
        SqlType = column.SqlType,
        DisplayName = column.DisplayName,
        DisplayNameArabic = column.DisplayNameArabic,
        IsHidden = column.IsHidden,
        IsFilterable = column.IsFilterable,
        IsGroupable = column.IsGroupable,
        IsIndexed = column.IsIndexed,
        DefaultSortDirection = SortDirections.Normalize(column.DefaultSortDirection),
        LookupId = column.LookupId,
        LookupName = column.Lookup is { IsValid: true } named ? named.Name : null,
        LookupNameArabic = column.Lookup is { IsValid: true } namedArabic ? namedArabic.NameArabic : null,
        LookupColumns = column.Lookup is { IsValid: true } lookup
            ? LookupDto.From(lookup).Columns
            : [],
        Ordinal = column.Ordinal
    };
}

public sealed class ReportAccessRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public static ReportAccessRoleDto From(ReportRoleAccess row) => new()
    {
        Id = row.RoleId,
        Name = row.Role?.Name ?? string.Empty,
        Description = row.Role?.Description
    };

    public static ReportAccessRoleDto From(AnalyticsRoleAccess row) => new()
    {
        Id = row.RoleId,
        Name = row.Role?.Name ?? string.Empty,
        Description = row.Role?.Description
    };

    public static ReportAccessRoleDto From(DatasetRoleAccess row) => new()
    {
        Id = row.RoleId,
        Name = row.Role?.Name ?? string.Empty,
        Description = row.Role?.Description
    };

    public static ReportAccessRoleDto From(ReportAccessRole role) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Description = role.Description
    };
}

public sealed class ReportPersonDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameArabic { get; set; }

    public static ReportPersonDto From(ReportPerson person) => new()
    {
        Id = person.Id,
        UserName = person.UserName,
        DisplayName = person.DisplayName,
        DisplayNameArabic = person.DisplayNameArabic
    };

    public static ReportPersonDto FromAccess(ReportAccess row) => FromUser(row.UserId, row.User);

    public static ReportPersonDto FromDatasetAccess(DatasetAccess row) => FromUser(row.UserId, row.User);

    private static ReportPersonDto FromUser(Guid userId, User? user) => new()
    {
        Id = userId,
        UserName = user?.UserName ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(user?.DisplayName)
            ? user?.UserName ?? "User"
            : user.DisplayName,
        DisplayNameArabic = user?.DisplayNameArabic
    };
}

public sealed class ReportCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static ReportCategoryDto From(ReportCategory category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        NameArabic = category.NameArabic,
        SortOrder = category.SortOrder,
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };
}

public sealed class ReportCategoryWriteDto
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ReportSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public string? DatasetNameArabic { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryNameArabic { get; set; }
    public int CategorySortOrder { get; set; }
    public string Kind { get; set; } = "table";
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public bool IsAdminCreated { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public bool CanEdit { get; set; }
    public bool CanSchedule { get; set; }
    public bool CanDelete { get; set; }
    public bool HasDraft { get; set; }
    public int PublishedVersion { get; set; }
    public int? DraftVersion { get; set; }
    public IReadOnlyList<Guid> AccessUserIds { get; set; } = [];
    public IReadOnlyList<ReportPersonDto> AccessUsers { get; set; } = [];
    public IReadOnlyList<Guid> AccessRoleIds { get; set; } = [];
    public IReadOnlyList<ReportAccessRoleDto> AccessRoles { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static ReportSummaryDto From(
        ReportTemplate template,
        ICurrentUser user,
        IReadOnlyDictionary<Guid, string> names)
    {
        var canDesign = ReportAccessRules.CanDesign(template, user);
        return new()
        {
            Id = template.Id,
            Name = template.Name,
            NameArabic = template.NameArabic,
            Description = template.Description,
            DescriptionArabic = template.DescriptionArabic,
            DatasetId = template.DatasetId,
            DatasetName = template.Dataset.DisplayName,
            DatasetNameArabic = template.Dataset.DisplayNameArabic,
            CategoryId = template.CategoryId,
            CategoryName = template.Category?.Name,
            CategoryNameArabic = template.Category?.NameArabic,
            CategorySortOrder = template.Category?.SortOrder ?? int.MaxValue,
            Kind = LayoutJson.KindOf(template.LayoutJson),
            Status = template.Status,
            CreatedBy = template.CreatedBy,
            CreatedByName = template.CreatedBy is Guid creator
                ? names.GetValueOrDefault(creator)
                : null,
            IsAdminCreated = template.IsAdminCreated,
            IsRestricted = template.IsRestricted,
            AccessMode = ReportAccessModes.Normalize(template.AccessMode, template.IsRestricted),
            CanEdit = canDesign && user.Has(PermissionCodes.ReportsUpdate),
            CanSchedule = canDesign && user.Has(PermissionCodes.ReportsSchedule),
            CanDelete = canDesign && user.Has(PermissionCodes.ReportsDelete),
            HasDraft = template.HasDraft,
            PublishedVersion = template.PublishedVersion,
            DraftVersion = template.Draft?.Version,
            AccessUserIds = template.Access.Select(row => row.UserId).ToList(),
            AccessUsers = template.Access.Select(ReportPersonDto.FromAccess).ToList(),
            AccessRoleIds = template.RoleAccess.Select(row => row.RoleId).ToList(),
            AccessRoles = template.RoleAccess.Select(ReportAccessRoleDto.From).ToList(),
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }
}

public sealed class ReportDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public string? DatasetNameArabic { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryNameArabic { get; set; }
    public int CategorySortOrder { get; set; }
    public string Kind { get; set; } = "table";
    public ReportLayout Layout { get; set; } = new();
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public bool IsAdminCreated { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool HasDraft { get; set; }
    public int PublishedVersion { get; set; }
    public int? DraftVersion { get; set; }
    public ReportDraftDto? Draft { get; set; }
    public IReadOnlyList<Guid> AccessUserIds { get; set; } = [];
    public IReadOnlyList<ReportPersonDto> AccessUsers { get; set; } = [];
    public IReadOnlyList<Guid> AccessRoleIds { get; set; } = [];
    public IReadOnlyList<ReportAccessRoleDto> AccessRoles { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DatasetColumnDto> FilterColumns { get; set; } = [];

    public static ReportDetailDto From(
        ReportTemplate template,
        ICurrentUser user,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyList<DatasetColumnDto>? filterColumns = null)
    {
        var summary = ReportSummaryDto.From(template, user, names);
        return new()
        {
            Id = summary.Id,
            Name = summary.Name,
            NameArabic = summary.NameArabic,
            Description = summary.Description,
            DescriptionArabic = summary.DescriptionArabic,
            DatasetId = summary.DatasetId,
            DatasetName = summary.DatasetName,
            DatasetNameArabic = summary.DatasetNameArabic,
            CategoryId = summary.CategoryId,
            CategoryName = summary.CategoryName,
            CategoryNameArabic = summary.CategoryNameArabic,
            CategorySortOrder = summary.CategorySortOrder,
            Kind = summary.Kind,
            Layout = LayoutJson.Parse(template.LayoutJson),
            Status = summary.Status,
            CreatedBy = summary.CreatedBy,
            CreatedByName = summary.CreatedByName,
            IsAdminCreated = summary.IsAdminCreated,
            IsRestricted = summary.IsRestricted,
            AccessMode = summary.AccessMode,
            CanEdit = summary.CanEdit,
            CanDelete = summary.CanDelete,
            HasDraft = summary.HasDraft,
            PublishedVersion = summary.PublishedVersion,
            DraftVersion = summary.DraftVersion,
            Draft = summary.CanEdit ? ReportDraftDto.From(template.Draft) : null,
            AccessUserIds = summary.AccessUserIds,
            AccessUsers = summary.AccessUsers,
            AccessRoleIds = summary.AccessRoleIds,
            AccessRoles = summary.AccessRoles,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            FilterColumns = filterColumns?.ToList() ?? []
        };
    }
}

public sealed class ReportDraftDto
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsRestricted { get; set; }
    public string AccessMode { get; set; } = ReportAccessModes.Everyone;
    public ReportLayout Layout { get; set; } = new();
    public List<Guid> AccessUserIds { get; set; } = [];
    public List<Guid> AccessRoleIds { get; set; } = [];

    public static ReportDraftDto? From(ReportDraftSnapshot? draft)
        => draft is null
            ? null
            : new()
            {
                Version = draft.Version,
                Name = draft.Name,
                NameArabic = draft.NameArabic,
                Description = draft.Description,
                DescriptionArabic = draft.DescriptionArabic,
                DatasetId = draft.DatasetId,
                CategoryId = draft.CategoryId,
                IsRestricted = draft.IsRestricted,
                AccessMode = ReportAccessModes.Normalize(draft.AccessMode, draft.IsRestricted),
                Layout = draft.Layout,
                AccessUserIds = draft.AccessUserIds,
                AccessRoleIds = draft.AccessRoleIds
            };
}

public sealed class ReportWriteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public ReportLayout Layout { get; set; } = new();
    public ReportStatus? Status { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsRestricted { get; set; }
    public string? AccessMode { get; set; }
    public List<Guid>? AccessUserIds { get; set; }
    public List<Guid>? AccessRoleIds { get; set; }
}

public sealed class ValidateExpressionRequest
{
    public Guid DatasetId { get; set; }
    public string? Expression { get; set; }
    public bool AllowAggregates { get; set; }
}

public sealed class PreviewRequest
{
    public Guid DatasetId { get; set; }
    public ReportLayout Layout { get; set; } = new();
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
    public string? Name { get; set; }
    public string? NameArabic { get; set; }
    public string? Lang { get; set; }
}

public sealed class DetailRowsRequest
{
    public string ParentValue { get; set; } = string.Empty;
    public int Skip { get; set; }
    public int Take { get; set; } = 20;
    public ReportLayout? Layout { get; set; }
    public string? Lang { get; set; }
}

public sealed class RunRequest
{
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
    /// <summary>Optional layout override (e.g. viewer-applied filters). Falls back to the saved template layout.</summary>
    public ReportLayout? Layout { get; set; }
    public string? Lang { get; set; }
}

public sealed class AiAssistRequest
{
    public string Prompt { get; set; } = string.Empty;
    public Guid? DatasetId { get; set; }
    public string? Name { get; set; }
    public string? NameArabic { get; set; }
    public string? Description { get; set; }
    public string? DescriptionArabic { get; set; }
    public ReportLayout? Layout { get; set; }
}

public sealed class AiStatusDto
{
    public bool Configured { get; set; }
    public string Model { get; set; } = string.Empty;
}

public sealed class AiAssistDto
{
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? DescriptionArabic { get; set; }
    public Guid DatasetId { get; set; }
    public string DatasetName { get; set; } = string.Empty;
    public ReportLayout Layout { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ColumnValuesRequest
{
    public string Field { get; set; } = string.Empty;
    public int Take { get; set; } = 200;
    public List<LayoutFilter> Filters { get; set; } = [];
    public string? Lang { get; set; }
}

public sealed class ColumnValuesDto
{
    public string Field { get; set; } = string.Empty;
    public List<string> Values { get; set; } = [];
    public List<ColumnValueChoiceDto> Choices { get; set; } = [];
    public bool Truncated { get; set; }

    public static ColumnValuesDto From(ColumnValuesResult result) => new()
    {
        Field = result.Field,
        Values = result.Values.ToList(),
        Choices = result.Choices.Select(choice => new ColumnValueChoiceDto
        {
            Value = choice.Value,
            Label = choice.Label
        }).ToList(),
        Truncated = result.Truncated
    };
}

public sealed class ColumnValueChoiceDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class DatasetSqlScriptDto
{
    public Guid DatasetId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;

    public static DatasetSqlScriptDto From(DatasetSqlScript script) => new()
    {
        DatasetId = script.DatasetId,
        SchemaName = script.SchemaName,
        ObjectName = script.ObjectName,
        ObjectType = script.ObjectType,
        Script = script.Script
    };
}
