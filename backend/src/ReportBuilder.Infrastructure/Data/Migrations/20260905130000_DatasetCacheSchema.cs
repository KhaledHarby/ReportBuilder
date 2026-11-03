using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetCacheSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'cache')
                    EXEC(N'CREATE SCHEMA [cache]');

                -- Move any early snapshots that landed in rb into cache.
                DECLARE @name sysname;
                DECLARE @sql nvarchar(600);
                DECLARE c CURSOR LOCAL FAST_FORWARD FOR
                    SELECT t.name
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = N'rb'
                      AND t.name LIKE N'DsCache[_]%';
                OPEN c;
                FETCH NEXT FROM c INTO @name;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    IF OBJECT_ID(N'cache.' + @name, N'U') IS NULL
                    BEGIN
                        SET @sql = N'ALTER SCHEMA [cache] TRANSFER [rb].' + QUOTENAME(@name) + N';';
                        EXEC(@sql);
                    END
                    ELSE
                    BEGIN
                        SET @sql = N'DROP TABLE [rb].' + QUOTENAME(@name) + N';';
                        EXEC(@sql);
                    END
                    FETCH NEXT FROM c INTO @name;
                END
                CLOSE c;
                DEALLOCATE c;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep [cache] schema; snapshots may still exist. No automatic reverse transfer.
        }
    }
}
