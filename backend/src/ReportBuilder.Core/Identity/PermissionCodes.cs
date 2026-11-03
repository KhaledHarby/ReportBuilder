namespace ReportBuilder.Core.Identity;

public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";
    public const string UsersActivate = "users.activate";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesDelete = "roles.delete";

    public const string PermissionsView = "permissions.view";
    public const string PermissionsCreate = "permissions.create";
    public const string PermissionsUpdate = "permissions.update";
    public const string PermissionsDelete = "permissions.delete";

    public const string AssignmentsManage = "assignments.manage";

    public const string ReportsView = "reports.view";
    public const string ReportsCreate = "reports.create";
    public const string ReportsUpdate = "reports.update";
    public const string ReportsDelete = "reports.delete";
    public const string ReportsExport = "reports.export";
    public const string ReportsSchedule = "reports.schedule";
    public const string ReportsDelivery = "reports.delivery";

    public const string DatasetsView = "datasets.view";
    public const string DatasetsUpdate = "datasets.update";
    public const string LookupsView = "lookups.view";
    public const string LookupsUpdate = "lookups.update";
    public const string DatasetsDiscover = "datasets.discover";
    public const string DatasetsCache = "datasets.cache";
    public const string SqlView = "sql.view";
    public const string SqlModify = "sql.modify";

    public const string AiUse = "ai.use";

    public const string LogsView = "logs.view";
    public const string LogsManage = "logs.manage";

    public const string InsightsView = "insights.view";

    public const string SettingsView = "settings.view";
    public const string SettingsUpdate = "settings.update";

    public const string AnalyticsView = "analytics.view";
    public const string AnalyticsCreate = "analytics.create";
    public const string AnalyticsUpdate = "analytics.update";
    public const string AnalyticsDelete = "analytics.delete";

    public static IReadOnlyList<(string Code, string Name, string Description)> Catalog { get; } =
    [
        (UsersView, "View users", "List and open user accounts"),
        (UsersCreate, "Create users", "Create user accounts"),
        (UsersUpdate, "Update users", "Edit user profile and credentials"),
        (UsersDelete, "Delete users", "Permanently remove user accounts"),
        (UsersActivate, "Activate users", "Activate or deactivate user accounts"),
        (RolesView, "View roles", "List and open roles"),
        (RolesCreate, "Create roles", "Create roles"),
        (RolesUpdate, "Update roles", "Edit role details"),
        (RolesDelete, "Delete roles", "Remove roles that are not system-protected"),
        (PermissionsView, "View permissions", "List permissions"),
        (PermissionsCreate, "Create permissions", "Create custom permissions"),
        (PermissionsUpdate, "Update permissions", "Edit custom permissions"),
        (PermissionsDelete, "Delete permissions", "Remove permissions that are not system-protected"),
        (AssignmentsManage, "Manage assignments", "Assign roles and permissions to users and roles"),
        (ReportsView, "View reports", "Open and run report templates"),
        (ReportsCreate, "Create reports", "Save new report templates"),
        (ReportsUpdate, "Update reports", "Edit report templates"),
        (ReportsDelete, "Delete reports", "Delete report templates"),
        (ReportsExport, "Export reports", "Download CSV, PDF, and Word"),
        (ReportsSchedule, "Schedule reports", "Configure and send scheduled report mail"),
        (ReportsDelivery, "View delivery", "Open delivery health for scheduled reports"),
        (DatasetsView, "View catalog", "Browse enabled data sources"),
        (DatasetsUpdate, "Update catalog", "Enable sources, import missing columns, and edit column settings"),
        (LookupsView, "View lookups", "Open lookup definitions and their columns"),
        (LookupsUpdate, "Update lookups", "Create, edit, and delete lookup definitions"),
        (DatasetsDiscover, "Discover catalog", "Scan SQL Server for tables and views"),
        (DatasetsCache, "Manage dataset cache", "Enable dataset snapshots, set refresh schedules, refresh, and clear cache"),
        (SqlView, "View SQL", "Open the SQL script for a catalog table or view"),
        (SqlModify, "Modify SQL", "Edit the SQL script for a catalog object and apply it to the database"),
        (AiUse, "Use Copilot", "Generate report layouts with Copilot"),
        (LogsView, "Access activity log", "Open the Activity page and search who did what"),
        (LogsManage, "Manage activity log", "Change log retention, and deactivate or sign people out from a log row"),
        (InsightsView, "View insights", "Open report usage and data freshness insights"),
        (SettingsView, "View settings", "Open application settings, including dataset cache providers"),
        (SettingsUpdate, "Update settings", "Change application settings and cache provider connections"),
        (AnalyticsView, "View analytics", "Open associative analytics dashboards"),
        (AnalyticsCreate, "Create analytics", "Create associative analytics dashboards"),
        (AnalyticsUpdate, "Update analytics", "Edit associative analytics dashboards"),
        (AnalyticsDelete, "Delete analytics", "Delete associative analytics dashboards")
    ];

    public static IReadOnlyList<string> All => Catalog.Select(item => item.Code).ToList();
}
