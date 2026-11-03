using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace ReportBuilder.Infrastructure.Data;

internal static class DatabaseStartup
{
    private const string ProductVersion = "10.0.11";

    public static async Task ApplyMigrationsAsync(
        ReportBuilderDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'rb')
                EXEC(N'CREATE SCHEMA [rb]');
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'cache')
                EXEC(N'CREATE SCHEMA [cache]');
            """,
            cancellationToken);

        await RecoverFailedRebuildsAsync(db, logger, cancellationToken);

        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await CopyHistoryFromDboAsync(db, logger, cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        foreach (var migrationId in pending)
        {
            if (!await AlreadyPresentAsync(db, migrationId, cancellationToken))
            {
                continue;
            }

            logger.LogInformation(
                "Skipping EF migration {MigrationId} because the matching objects already exist. Recording it in history only.",
                migrationId);
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, ProductVersion)),
                cancellationToken);
        }

        await using var migrateDb = new ReportBuilderDbContext(
            db.GetService<DbContextOptions<ReportBuilderDbContext>>());
        await migrateDb.Database.MigrateAsync(cancellationToken);
    }

    private static async Task CopyHistoryFromDboAsync(
        ReportBuilderDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var copied = await ScalarIntAsync(
            db,
            """
            IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
                SELECT 0;
            ELSE
            BEGIN
                INSERT INTO [rb].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                SELECT d.[MigrationId], d.[ProductVersion]
                FROM [dbo].[__EFMigrationsHistory] AS d
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [rb].[__EFMigrationsHistory] AS r
                    WHERE r.[MigrationId] = d.[MigrationId]);
                SELECT @@ROWCOUNT;
            END
            """,
            cancellationToken);

        if (copied > 0)
        {
            logger.LogInformation(
                "Copied {Count} EF migration history row(s) from dbo.__EFMigrationsHistory into rb.",
                copied);
        }
    }

    private static async Task RecoverFailedRebuildsAsync(
        ReportBuilderDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "Datasets", "DatasetColumns", "ReportTemplates", "Users" })
        {
            var tmpExists = await TableExistsAsync(db, $"rb.{table}_tmp", cancellationToken);
            var originalExists = await TableExistsAsync(db, $"rb.{table}", cancellationToken);
            if (tmpExists && originalExists)
            {
                logger.LogWarning("Dropping leftover {Table}_tmp from a failed rebuild. {Table} was not dropped.", table, table);
            }
            else if (tmpExists)
            {
                logger.LogWarning("Renaming {Table}_tmp back to {Table} after a failed rebuild.", table, table);
            }
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'rb.Datasets_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.Datasets', N'U') IS NOT NULL
                DROP TABLE [rb].[Datasets_tmp];
            IF OBJECT_ID(N'rb.Datasets_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.Datasets', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[rb].[Datasets_tmp]', N'Datasets';
                IF NOT EXISTS (
                    SELECT 1 FROM sys.key_constraints
                    WHERE [name] = N'PK_Datasets' AND [parent_object_id] = OBJECT_ID(N'rb.Datasets'))
                    ALTER TABLE [rb].[Datasets] ADD CONSTRAINT [PK_Datasets] PRIMARY KEY ([Id]);
            END

            IF OBJECT_ID(N'rb.DatasetColumns_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.DatasetColumns', N'U') IS NOT NULL
                DROP TABLE [rb].[DatasetColumns_tmp];
            IF OBJECT_ID(N'rb.DatasetColumns_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.DatasetColumns', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[rb].[DatasetColumns_tmp]', N'DatasetColumns';
                IF NOT EXISTS (
                    SELECT 1 FROM sys.key_constraints
                    WHERE [name] = N'PK_DatasetColumns' AND [parent_object_id] = OBJECT_ID(N'rb.DatasetColumns'))
                    ALTER TABLE [rb].[DatasetColumns] ADD CONSTRAINT [PK_DatasetColumns] PRIMARY KEY ([Id]);
            END

            IF OBJECT_ID(N'rb.ReportTemplates_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.ReportTemplates', N'U') IS NOT NULL
                DROP TABLE [rb].[ReportTemplates_tmp];
            IF OBJECT_ID(N'rb.ReportTemplates_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.ReportTemplates', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[rb].[ReportTemplates_tmp]', N'ReportTemplates';
                IF NOT EXISTS (
                    SELECT 1 FROM sys.key_constraints
                    WHERE [name] = N'PK_ReportTemplates' AND [parent_object_id] = OBJECT_ID(N'rb.ReportTemplates'))
                    ALTER TABLE [rb].[ReportTemplates] ADD CONSTRAINT [PK_ReportTemplates] PRIMARY KEY ([Id]);
            END

            IF OBJECT_ID(N'rb.Users_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.Users', N'U') IS NOT NULL
                DROP TABLE [rb].[Users_tmp];
            IF OBJECT_ID(N'rb.Users_tmp', N'U') IS NOT NULL AND OBJECT_ID(N'rb.Users', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[rb].[Users_tmp]', N'Users';
                IF NOT EXISTS (
                    SELECT 1 FROM sys.key_constraints
                    WHERE [name] = N'PK_Users' AND [parent_object_id] = OBJECT_ID(N'rb.Users'))
                    ALTER TABLE [rb].[Users] ADD CONSTRAINT [PK_Users] PRIMARY KEY ([Id]);
            END
            """,
            cancellationToken);
    }

    private static async Task<bool> AlreadyPresentAsync(
        ReportBuilderDbContext db,
        string migrationId,
        CancellationToken cancellationToken)
    {
        return migrationId switch
        {
            "20260825131518_InitialCreate" =>
                await TableExistsAsync(db, "rb.Datasets", cancellationToken),
            "20260827185919_Identity" =>
                await TableExistsAsync(db, "rb.Users", cancellationToken),
            "20260827192322_ReportStatus" =>
                await ColumnExistsAsync(db, "rb.ReportTemplates", "Status", cancellationToken),
            "20260827194801_ReportAccess" =>
                await TableExistsAsync(db, "rb.ReportAccess", cancellationToken),
            "20260828000000_ArabicDisplayNames" =>
                await ColumnExistsAsync(db, "rb.Datasets", "DisplayNameArabic", cancellationToken),
            "20260828020000_ReorderArabicColumns" =>
                await TableExistsAsync(db, "rb.Datasets", cancellationToken),
            "20260828030000_DatasetAccess" =>
                await TableExistsAsync(db, "rb.DatasetAccess", cancellationToken)
                || await ColumnExistsAsync(db, "rb.Datasets", "IsRestricted", cancellationToken),
            "20260828040000_UserDisplayNameArabic" =>
                await ColumnExistsAsync(db, "rb.Users", "DisplayNameArabic", cancellationToken),
            "20260828140832_ActivityLogs" =>
                await TableExistsAsync(db, "rb.ActivityLogs", cancellationToken),
            "20260828141117_ActivityLogsModelFix" =>
                await TableExistsAsync(db, "rb.ActivityLogs", cancellationToken),
            "20260831091026_DatasetSortOrder" =>
                await ColumnExistsAsync(db, "rb.Datasets", "SortOrder", cancellationToken),
            "20260831153000_DatasetColumnDefaultSortDirection" =>
                await ColumnExistsAsync(db, "rb.DatasetColumns", "DefaultSortDirection", cancellationToken),
            "20260914120000_DatasetColumnIsIndexed" =>
                await ColumnExistsAsync(db, "rb.DatasetColumns", "IsIndexed", cancellationToken),
            "20260916140000_CacheProviders" =>
                await TableExistsAsync(db, "rb.CacheSettings", cancellationToken)
                && await ColumnExistsAsync(db, "rb.DatasetCacheStates", "CacheProvider", cancellationToken),
            "20260917120000_ReportCategories" =>
                await TableExistsAsync(db, "rb.ReportCategories", cancellationToken)
                && await ColumnExistsAsync(db, "rb.ReportTemplates", "CategoryId", cancellationToken),
            "20260923103000_Lookups" =>
                await TableExistsAsync(db, "rb.Lookups", cancellationToken)
                && await ColumnExistsAsync(db, "rb.DatasetColumns", "LookupId", cancellationToken),
            "20260918120000_ReportDraftVersions" =>
                await ColumnExistsAsync(db, "rb.ReportTemplates", "DraftJson", cancellationToken)
                && await ColumnExistsAsync(db, "rb.ReportTemplates", "PublishedVersion", cancellationToken),
            "20260902120000_UserSecurityStamp" =>
                await ColumnExistsAsync(db, "rb.Users", "SecurityStamp", cancellationToken),
            _ => false
        };
    }

    private static async Task<bool> TableExistsAsync(
        ReportBuilderDbContext db,
        string objectName,
        CancellationToken cancellationToken)
    {
        return await ScalarIntAsync(
            db,
            "SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NULL THEN 0 ELSE 1 END",
            cancellationToken,
            ("@name", objectName)) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        ReportBuilderDbContext db,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        return await ScalarIntAsync(
            db,
            "SELECT CASE WHEN COL_LENGTH(@table, @column) IS NULL THEN 0 ELSE 1 END",
            cancellationToken,
            ("@table", table),
            ("@column", column)) == 1;
    }

    private static async Task<int> ScalarIntAsync(
        ReportBuilderDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? 0 : Convert.ToInt32(result);
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
