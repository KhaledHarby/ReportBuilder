using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ReportBuilder.Infrastructure.Data;

public sealed class ReportBuilderDbContextFactory : IDesignTimeDbContextFactory<ReportBuilderDbContext>
{
    public ReportBuilderDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("ReportBuilder")
            ?? throw new InvalidOperationException(
                "Connection string 'ReportBuilder' was not found in appsettings.");

        var options = new DbContextOptionsBuilder<ReportBuilderDbContext>()
            .UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "rb");
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                })
            .Options;

        return new ReportBuilderDbContext(options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var apiContentRoot = ResolveApiContentRoot();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        return new ConfigurationBuilder()
            .SetBasePath(apiContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string ResolveApiContentRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            foreach (var relative in new[]
                     {
                         Path.Combine("src", "ReportBuilder.Api"),
                         "ReportBuilder.Api",
                     })
            {
                var candidate = Path.Combine(current.FullName, relative);
                if (File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    return candidate;
                }
            }

            if (File.Exists(Path.Combine(current.FullName, "appsettings.json"))
                && File.Exists(Path.Combine(current.FullName, "ReportBuilder.Api.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate ReportBuilder.Api appsettings.json for design-time DbContext creation.");
    }
}
