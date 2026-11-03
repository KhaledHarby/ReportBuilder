using Microsoft.EntityFrameworkCore;
using ReportBuilder.Infrastructure.Data;

namespace ReportBuilder.Infrastructure.Data;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(ReportBuilderDbContext db, CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Customers (
                    CustomerId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CustomerName NVARCHAR(100) NOT NULL,
                    Region NVARCHAR(50) NOT NULL,
                    Status NVARCHAR(20) NOT NULL
                );

                INSERT INTO dbo.Customers (CustomerName, Region, Status) VALUES
                    (N'Northwind Trading', N'North', N'Active'),
                    (N'Contoso Retail', N'South', N'Active'),
                    (N'Fabrikam Wholesale', N'West', N'Active'),
                    (N'Adventure Works', N'East', N'Active'),
                    (N'Wide World Importers', N'North', N'Inactive'),
                    (N'Litware Partners', N'West', N'Active'),
                    (N'Alpine Ski House', N'East', N'Active'),
                    (N'Blue Yonder', N'South', N'Active');
            END;

            IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Orders (
                    OrderId INT IDENTITY(1001,1) NOT NULL PRIMARY KEY,
                    CustomerId INT NOT NULL,
                    OrderDate DATE NOT NULL,
                    Status NVARCHAR(20) NOT NULL,
                    Amount DECIMAL(12,2) NOT NULL,
                    Quantity INT NOT NULL,
                    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId)
                );

                INSERT INTO dbo.Orders (CustomerId, OrderDate, Status, Amount, Quantity) VALUES
                    (1, '2026-01-12', N'Open', 1250.00, 10),
                    (1, '2026-02-03', N'Shipped', 890.50, 4),
                    (2, '2026-01-20', N'Shipped', 2400.00, 12),
                    (2, '2026-03-11', N'Open', 610.00, 3),
                    (3, '2026-02-18', N'Cancelled', 150.00, 1),
                    (3, '2026-04-02', N'Shipped', 3320.75, 18),
                    (4, '2026-01-08', N'Shipped', 980.00, 6),
                    (4, '2026-03-22', N'Open', 1740.00, 9),
                    (5, '2025-12-19', N'Shipped', 220.00, 2),
                    (6, '2026-02-27', N'Shipped', 4510.00, 20),
                    (6, '2026-04-14', N'Open', 870.25, 5),
                    (7, '2026-03-01', N'Shipped', 1340.00, 7),
                    (7, '2026-05-09', N'Open', 260.00, 2),
                    (8, '2026-01-30', N'Shipped', 1999.99, 8),
                    (8, '2026-04-21', N'Shipped', 640.00, 4),
                    (1, '2026-05-02', N'Open', 3100.00, 15),
                    (2, '2026-05-16', N'Shipped', 775.00, 5),
                    (4, '2026-06-04', N'Open', 512.40, 3),
                    (6, '2026-06-18', N'Shipped', 2890.00, 11),
                    (3, '2026-07-07', N'Open', 1180.00, 6);
            END;

            IF OBJECT_ID(N'dbo.vw_OrderSummary', N'V') IS NULL
            BEGIN
                EXEC(N'
                    CREATE VIEW dbo.vw_OrderSummary AS
                    SELECT
                        o.OrderId,
                        c.CustomerName,
                        c.Region,
                        o.OrderDate,
                        o.Status,
                        o.Amount,
                        o.Quantity
                    FROM dbo.Orders o
                    INNER JOIN dbo.Customers c ON c.CustomerId = o.CustomerId
                ');
            END;
            """;

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
