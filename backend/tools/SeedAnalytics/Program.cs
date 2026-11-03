using Microsoft.EntityFrameworkCore;
using ReportBuilder.Infrastructure.Data;

for (var attempt = 1; attempt <= 6; attempt++)
{
    try
    {
        using var db = new ReportBuilderDbContextFactory().CreateDbContext([]);
        var catalog = await db.Datasets.AsNoTracking()
            .Include(dataset => dataset.Columns)
            .Where(dataset => dataset.IsEnabled)
            .OrderBy(dataset => dataset.SortOrder)
            .ThenBy(dataset => dataset.DisplayName)
            .ToListAsync();
        foreach (var dataset in catalog)
        {
            Console.WriteLine($"DATASET {dataset.SchemaName}.{dataset.ObjectName} | {dataset.DisplayName} | {dataset.Id}");
            foreach (var column in dataset.Columns.Where(item => !item.IsHidden).OrderBy(item => item.Ordinal))
            {
                Console.WriteLine($"  {column.ColumnName} | {column.SqlType} | {column.DisplayName} | group={column.IsGroupable} filter={column.IsFilterable}");
            }
        }

        var notes = await AnalyticsExamplesSeeder.SeedAsync(db);
        foreach (var note in notes)
        {
            Console.WriteLine(note);
        }

        var dashboards = await db.AnalyticsDashboards.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => item.Name + " / " + (item.NameArabic ?? "-") + " [" + item.Status + "]")
            .ToListAsync();
        Console.WriteLine($"Analytics dashboards in database: {dashboards.Count}");
        foreach (var name in dashboards)
        {
            Console.WriteLine($"- {name}");
        }

        return;
    }
    catch (Exception ex) when (attempt < 6)
    {
        Console.WriteLine($"Attempt {attempt} failed: {ex.GetBaseException().Message}");
        await Task.Delay(TimeSpan.FromSeconds(4));
    }
}
