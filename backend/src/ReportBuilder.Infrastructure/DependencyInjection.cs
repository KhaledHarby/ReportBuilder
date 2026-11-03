using System.Net;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Services;
using ReportBuilder.Infrastructure.Cache;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Identity;
using ReportBuilder.Infrastructure.Options;
using ReportBuilder.Infrastructure.Services;

namespace ReportBuilder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportBuilderInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<ReportBuilderDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql =>
                {
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "rb");
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                }));

        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<JwtTokenFactory>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();
        services.AddScoped<IPermissionAdminService, PermissionAdminService>();

        services.AddScoped<IDatasetCatalogService, DatasetCatalogService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IDatasetCacheService, DatasetCacheService>();
        services.AddScoped<ICacheSettingsService, CacheSettingsService>();
        services.AddScoped<IAppBrandingService, AppBrandingService>();
        services.AddScoped<ISmtpProfileService, SmtpProfileService>();
        services.AddScoped<IAiConnectionService, AiConnectionService>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IReportScheduleService, ReportScheduleService>();
        services.AddScoped<IReportScheduleRunner, ReportScheduleRunner>();
        services.AddScoped<ClickHouseHttpClient>();
        services.AddScoped<ClickHouseDatasetCacheStore>();
        services.AddSingleton<RedisCacheClient>();
        services.AddScoped<RedisDatasetCacheStore>();
        services.AddSingleton<DatasetCacheRefreshQueue>();
        services.AddSingleton<MongoCacheClient>();
        services.AddScoped<MongoDatasetCacheStore>();
        services.AddSingleton<PostgresCacheClient>();
        services.AddScoped<PostgresDatasetCacheStore>();
        services.AddHttpClient(ClickHouseHttpClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        }).ConfigurePrimaryHttpMessageHandler(ClickHouseHttpClient.CreateHandler);
        services.AddScoped<IReportQueryService, ReportQueryService>();
        services.AddScoped<IReportTemplateService, ReportTemplateService>();
        services.AddScoped<IReportCategoryService, ReportCategoryService>();
        services.AddScoped<IReportInsightsService, ReportInsightsService>();
        services.AddScoped<IAnalyticsDashboardService, AnalyticsDashboardService>();
        services.AddMemoryCache();
        if (AnalyticsSqlLogOptions.IsEnabled(configuration))
        {
            services.AddSingleton(Channel.CreateBounded<AnalyticsSqlLogFile>(
                new BoundedChannelOptions(256)
                {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true
                }));
            services.AddSingleton<IAnalyticsSqlLog, AnalyticsSqlFileLog>();
        }
        else
        {
            services.AddSingleton<IAnalyticsSqlLog, NullAnalyticsSqlLog>();
        }

        services.AddScoped<IAnalyticsQueryService, AnalyticsQueryService>();
        services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddScoped<IReportAiService, ReportAiService>();
        services.AddScoped<IActivityLog, ActivityLogService>();
        return services;
    }

    public static async Task ApplyDatabaseMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ReportBuilder.DatabaseStartup");
        await DatabaseStartup.ApplyMigrationsAsync(db, logger, cancellationToken);
    }

    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();

        if (options.MigrateOnStartup)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ReportBuilder.DatabaseStartup");
            await DatabaseStartup.ApplyMigrationsAsync(db, logger, cancellationToken);
            await EmailTemplateSeeder.EnsureDeliveryTemplateAsync(db, cancellationToken);
            await EmailTemplateSeeder.EnsurePasswordResetTemplateAsync(db, cancellationToken);
            var openAi = scope.ServiceProvider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
            await AiConnectionSeeder.EnsureFromOptionsAsync(db, openAi, cancellationToken);
        }

        if (!options.SeedOnStartup)
        {
            return;
        }

        await SeedIdentityAsync(services, cancellationToken);

        var connectionString = db.Database.GetConnectionString() ?? string.Empty;
        var isLocalDemo = connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase);

        if (isLocalDemo)
        {
            await DemoDataSeeder.SeedAsync(db, cancellationToken);
        }

        var catalog = scope.ServiceProvider.GetRequiredService<IDatasetCatalogService>();
        if (!await db.Datasets.AnyAsync(cancellationToken))
        {
            await catalog.DiscoverAsync(cancellationToken);
        }

        await ChartExamplesSeeder.SeedAsync(db, cancellationToken);
        await AnalyticsExamplesSeeder.SeedAsync(db, cancellationToken);

        var activity = scope.ServiceProvider.GetRequiredService<IActivityLog>();
        await activity.GetSettingsAsync(cancellationToken);

        if (!isLocalDemo)
        {
            return;
        }

        db.ChangeTracker.Clear();

        var demoIds = await db.Datasets.AsNoTracking()
            .Where(d => d.SchemaName == "dbo"
                        && (d.ObjectName == "Customers" || d.ObjectName == "Orders" || d.ObjectName == "vw_OrderSummary")
                        && !d.IsEnabled)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in demoIds)
        {
            try
            {
                using var enableScope = services.CreateScope();
                var enableCatalog = enableScope.ServiceProvider.GetRequiredService<IDatasetCatalogService>();
                await enableCatalog.UpdateAsync(id, new DatasetUpdateRequest { IsEnabled = true }, cancellationToken);
            }
            catch (Exception)
            {
                // Demo enable is best-effort; objects can still be enabled from the catalog UI.
            }
        }
    }

    public static async Task SeedIdentityAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
