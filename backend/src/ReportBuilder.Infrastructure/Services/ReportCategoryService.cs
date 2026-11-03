using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Query;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Services;

public sealed class ReportCategoryService : IReportCategoryService
{
    private readonly ReportBuilderDbContext _db;
    private readonly IActivityLog _log;

    public ReportCategoryService(ReportBuilderDbContext db, IActivityLog log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<ReportCategory>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.ReportCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public async Task<ReportCategory> CreateAsync(
        ReportCategoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var category = new ReportCategory
        {
            Id = Guid.NewGuid(),
            Name = await RequireUniqueName(null, request.Name, cancellationToken),
            NameArabic = EmptyToNull(request.NameArabic),
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.ReportCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report-category.create",
                Message = $"Created report category {category.Name}.",
                EntityType = "ReportCategory",
                EntityId = category.Id,
                EntityName = category.Name
            },
            cancellationToken);
        return category;
    }

    public async Task<ReportCategory?> UpdateAsync(
        Guid id,
        ReportCategoryWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.ReportCategories.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.Name = await RequireUniqueName(id, request.Name, cancellationToken);
        category.NameArabic = EmptyToNull(request.NameArabic);
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report-category.update",
                Message = $"Updated report category {category.Name}.",
                EntityType = "ReportCategory",
                EntityId = category.Id,
                EntityName = category.Name
            },
            cancellationToken);
        return category;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _db.ReportCategories.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (category is null)
        {
            return false;
        }

        _db.ReportCategories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);
        await _log.WriteAsync(
            new ActivityEvent
            {
                Category = ActivityCategories.Reports,
                Action = "report-category.delete",
                Message = $"Deleted report category {category.Name}.",
                EntityType = "ReportCategory",
                EntityId = category.Id,
                EntityName = category.Name
            },
            cancellationToken);
        return true;
    }

    private async Task<string> RequireUniqueName(
        Guid? id,
        string? name,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ReportQueryException("Category name is required.");
        }

        if (trimmed.Length < 2)
        {
            throw new ReportQueryException("Category name must be at least 2 characters.");
        }

        var taken = await _db.ReportCategories.AsNoTracking()
            .AnyAsync(
                row => row.Name == trimmed && (!id.HasValue || row.Id != id.Value),
                cancellationToken);
        if (taken)
        {
            throw new ReportQueryException("A category with this name already exists.");
        }

        return trimmed;
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
