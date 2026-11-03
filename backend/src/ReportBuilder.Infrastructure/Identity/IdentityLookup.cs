using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Identity;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Identity;

internal static class IdentityLookup
{
    public static async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        ReportBuilderDbContext db,
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken)
    {
        var keys = ids.Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var names = new Dictionary<Guid, string>
        {
            [IdentityDefaults.SystemActorId] = "System"
        };

        if (keys.Count == 0)
        {
            return names;
        }

        var rows = await db.Users.AsNoTracking()
            .Where(user => keys.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName, user.UserName })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            names[row.Id] = string.IsNullOrWhiteSpace(row.DisplayName) ? row.UserName : row.DisplayName;
        }

        return names;
    }
}
