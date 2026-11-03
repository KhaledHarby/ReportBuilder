using Microsoft.EntityFrameworkCore;
using ReportBuilder.Infrastructure.Data;

for (var attempt = 1; attempt <= 6; attempt++)
{
    try
    {
        using var db = new ReportBuilderDbContextFactory().CreateDbContext([]);
        await ChartExamplesSeeder.SeedAsync(db);
        var ids = new[]
        {
            ChartExamplesSeeder.FirmsByTypeId,
            ChartExamplesSeeder.FirmsByStatusId,
            ChartExamplesSeeder.LicenseExpiryId,
            ChartExamplesSeeder.TypeAndStatusId,
            ChartExamplesSeeder.EvaluationByTypeId
        };
        var names = await db.ReportTemplates.AsNoTracking()
            .Where(template => ids.Contains(template.Id))
            .Select(template => template.Name + " / " + template.NameArabic)
            .ToListAsync();
        Console.WriteLine($"Chart examples in database: {names.Count}");
        foreach (var name in names)
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

