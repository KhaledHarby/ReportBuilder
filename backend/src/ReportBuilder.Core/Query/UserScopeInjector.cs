using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Core.Query;

public static class UserScopeInjector
{
    public static ReportLayout Apply(ReportLayout layout, Dataset dataset, ICurrentUser user)
    {
        if (layout.UserScope?.Enabled != true)
        {
            return layout;
        }

        var scope = layout.UserScope;
        if (string.IsNullOrWhiteSpace(scope.Field))
        {
            throw new ReportQueryException("Choose a dataset column for current-user scoping.");
        }

        var property = UserScopeProperties.Normalize(scope.Property);
        var meta = dataset.Columns.FirstOrDefault(column =>
            column.ColumnName.Equals(scope.Field.Trim(), StringComparison.OrdinalIgnoreCase));

        if (meta is null || meta.IsHidden)
        {
            throw new ReportQueryException($"Unknown or hidden field '{scope.Field}'.");
        }

        if (!meta.IsFilterable)
        {
            throw new ReportQueryException($"Column '{meta.ColumnName}' cannot be used for user scoping.");
        }

        if (!UserScopeProperties.IsCompatible(property, meta.SqlType))
        {
            throw new ReportQueryException(
                $"User property '{property}' cannot be matched against column '{meta.ColumnName}' ({meta.SqlType}).");
        }

        if (!user.IsAuthenticated)
        {
            throw new ReportQueryException("Sign in to run this report.");
        }

        var value = UserScopeProperties.ResolveValue(property, user);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReportQueryException("Your account is missing the selected user property for this report.");
        }

        var filters = layout.Filters
            .Where(filter => !filter.Field.Equals(meta.ColumnName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        filters.Insert(0, new LayoutFilter
        {
            Field = meta.ColumnName,
            Operator = "eq",
            Value = value,
            IsFixed = true
        });

        return new ReportLayout
        {
            Kind = layout.Kind,
            Columns = layout.Columns,
            GroupBy = layout.GroupBy,
            Filters = filters,
            FiltersPerRow = layout.FiltersPerRow,
            OrderBy = layout.OrderBy,
            Chart = layout.Chart,
            UserScope = layout.UserScope,
            Detail = layout.Detail
        };
    }
}
