# Report Builder

Internal tool for designing **report templates** (layout only) against a whitelist of SQL Server tables and views. Opening a saved report always queries live data.

## Stack

- ASP.NET Core Web API (`backend/`)
- React + Vite (`frontend/`)
- SQL Server / LocalDB

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- SQL Server LocalDB or SQL Server. Default connection:

  `Server=(localdb)\mssqllocaldb;Database=ReportBuilder;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;`

  Change `backend/src/ReportBuilder.Api/appsettings.json` if you use a different instance.

## Run

```bash
# Restore local tools (dotnet-ef) if you need to add migrations
dotnet tool restore

# API (creates the database, demo tables, and catalog on first start)
dotnet run --project backend/src/ReportBuilder.Api --launch-profile http

# UI
cd frontend
npm install
npm run dev
```

Open [http://localhost:5173](http://localhost:5173). The Vite dev server proxies `/api` to `http://localhost:5244`.

## First-run demo data

On startup the API creates `dbo.Customers`, `dbo.Orders`, and `dbo.vw_OrderSummary`, then enables them in the catalog so you can design a report immediately.

## Notes

- There is no authentication in v1. Anyone who can reach the app can edit shared templates and the catalog.
- The browser never sends raw SQL. The API builds parameterized `SELECT` statements from the saved layout and catalog metadata.
