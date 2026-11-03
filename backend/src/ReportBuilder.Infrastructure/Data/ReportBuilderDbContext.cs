using Microsoft.EntityFrameworkCore;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Identity;
using ReportBuilder.Core.Models;

namespace ReportBuilder.Infrastructure.Data;

public class ReportBuilderDbContext : DbContext
{
    public ReportBuilderDbContext(DbContextOptions<ReportBuilderDbContext> options) : base(options)
    {
    }

    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetColumn> DatasetColumns => Set<DatasetColumn>();
    public DbSet<Lookup> Lookups => Set<Lookup>();
    public DbSet<DatasetAccess> DatasetAccess => Set<DatasetAccess>();
    public DbSet<DatasetRoleAccess> DatasetRoleAccess => Set<DatasetRoleAccess>();
    public DbSet<DatasetCacheState> DatasetCacheStates => Set<DatasetCacheState>();
    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
    public DbSet<ReportCategory> ReportCategories => Set<ReportCategory>();
    public DbSet<ReportAccess> ReportAccess => Set<ReportAccess>();
    public DbSet<ReportRoleAccess> ReportRoleAccess => Set<ReportRoleAccess>();
    public DbSet<AnalyticsDashboard> AnalyticsDashboards => Set<AnalyticsDashboard>();
    public DbSet<AnalyticsAccess> AnalyticsAccess => Set<AnalyticsAccess>();
    public DbSet<AnalyticsRoleAccess> AnalyticsRoleAccess => Set<AnalyticsRoleAccess>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LaunchCode> LaunchCodes => Set<LaunchCode>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<CacheSettings> CacheSettings => Set<CacheSettings>();
    public DbSet<SmtpProfile> SmtpProfiles => Set<SmtpProfile>();
    public DbSet<AiConnection> AiConnections => Set<AiConnection>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportScheduleRecipient> ReportScheduleRecipients => Set<ReportScheduleRecipient>();
    public DbSet<ReportScheduleRun> ReportScheduleRuns => Set<ReportScheduleRun>();
    public DbSet<ReportRevision> ReportRevisions => Set<ReportRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("rb");

        modelBuilder.Entity<Dataset>(entity =>
        {
            entity.ToTable("Datasets", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SchemaName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ObjectName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DisplayNameArabic).HasMaxLength(256);
            entity.Property(x => x.ObjectType).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SqlText);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.AccessMode).HasMaxLength(16).IsRequired().HasDefaultValue(ReportAccessModes.Everyone);
            entity.Property(x => x.IsCached).HasDefaultValue(false);
            entity.Property(x => x.CacheScheduleMode).HasMaxLength(16).HasDefaultValue(CacheScheduleModes.Manual);
            entity.Property(x => x.CacheDailyAtLocalTime).HasMaxLength(5);
            entity.HasIndex(x => new { x.SchemaName, x.ObjectName }).IsUnique();
            entity.HasMany(x => x.Columns)
                .WithOne(x => x.Dataset)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Access)
                .WithOne(x => x.Dataset)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.RoleAccess)
                .WithOne(x => x.Dataset)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Templates)
                .WithOne(x => x.Dataset)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CacheState)
                .WithOne(x => x.Dataset)
                .HasForeignKey<DatasetCacheState>(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DatasetCacheState>(entity =>
        {
            entity.ToTable("DatasetCacheStates", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.DatasetId);
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.CacheTableName).HasMaxLength(128);
            entity.Property(x => x.RefreshedByServer).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.Property(x => x.CacheProvider).HasMaxLength(32);
            entity.HasIndex(x => x.NextRefreshAt);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<DatasetColumn>(entity =>
        {
            entity.ToTable("DatasetColumns", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ColumnName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SqlType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DisplayNameArabic).HasMaxLength(256);
            entity.Property(x => x.DefaultSortDirection).HasMaxLength(4).HasDefaultValue(SortDirections.Desc);
            entity.Property(x => x.IsIndexed).HasDefaultValue(false);
            entity.HasIndex(x => new { x.DatasetId, x.ColumnName }).IsUnique();
            entity.HasIndex(x => x.LookupId);
            entity.HasOne(x => x.Lookup)
                .WithMany()
                .HasForeignKey(x => x.LookupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Lookup>(entity =>
        {
            entity.ToTable("Lookups", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NameArabic).HasMaxLength(200);
            entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.SchemaName).HasMaxLength(128);
            entity.Property(x => x.ObjectName).HasMaxLength(128);
            entity.Property(x => x.KeyColumn).HasMaxLength(128);
            entity.Property(x => x.LabelColumn).HasMaxLength(128);
            entity.Property(x => x.SortColumn).HasMaxLength(128);
            entity.Property(x => x.SortDirection).HasMaxLength(4).HasDefaultValue(SortDirections.Asc);
            entity.Property(x => x.ValidationError).HasMaxLength(500);
            entity.Property(x => x.ColumnsJson).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DatasetAccess>(entity =>
        {
            entity.ToTable("DatasetAccess");
            entity.HasKey(x => new { x.DatasetId, x.UserId });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DatasetRoleAccess>(entity =>
        {
            entity.ToTable("DatasetRoleAccess");
            entity.HasKey(x => new { x.DatasetId, x.RoleId });
            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReportCategory>(entity =>
        {
            entity.ToTable("ReportCategories", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NameArabic).HasMaxLength(120);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.SortOrder);
            entity.HasMany(x => x.Reports)
                .WithOne(x => x.Category)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReportTemplate>(entity =>
        {
            entity.ToTable("ReportTemplates", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NameArabic).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DescriptionArabic).HasMaxLength(1000);
            entity.Property(x => x.LayoutJson).IsRequired();
            entity.Ignore(x => x.Draft);
            entity.Ignore(x => x.HasDraft);
            entity.Property(x => x.DraftJson);
            entity.Property(x => x.PublishedVersion);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.AccessMode).HasMaxLength(16).IsRequired().HasDefaultValue(ReportAccessModes.Everyone);
            entity.HasIndex(x => x.CategoryId);
            entity.HasMany(x => x.Access)
                .WithOne(x => x.Report)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.RoleAccess)
                .WithOne(x => x.Report)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReportAccess>(entity =>
        {
            entity.ToTable("ReportAccess");
            entity.HasKey(x => new { x.ReportId, x.UserId });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReportRoleAccess>(entity =>
        {
            entity.ToTable("ReportRoleAccess");
            entity.HasKey(x => new { x.ReportId, x.RoleId });
            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyticsDashboard>(entity =>
        {
            entity.ToTable("AnalyticsDashboards", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NameArabic).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DescriptionArabic).HasMaxLength(1000);
            entity.Property(x => x.ConfigJson).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.IsRestricted).HasDefaultValue(false);
            entity.Property(x => x.AccessMode).HasMaxLength(16).IsRequired().HasDefaultValue(ReportAccessModes.Everyone);
            entity.HasIndex(x => x.UpdatedAt);
            entity.HasMany(x => x.Access)
                .WithOne(x => x.Dashboard)
                .HasForeignKey(x => x.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.RoleAccess)
                .WithOne(x => x.Dashboard)
                .HasForeignKey(x => x.DashboardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyticsAccess>(entity =>
        {
            entity.ToTable("AnalyticsAccess");
            entity.HasKey(x => new { x.DashboardId, x.UserId });
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyticsRoleAccess>(entity =>
        {
            entity.ToTable("AnalyticsRoleAccess");
            entity.HasKey(x => new { x.DashboardId, x.RoleId });
            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DisplayNameArabic).HasMaxLength(128);
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(128);
            entity.Property(x => x.ExternalSource).HasMaxLength(64);
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => new { x.ExternalSource, x.ExternalId })
                .IsUnique()
                .HasFilter("[ExternalId] IS NOT NULL AND [ExternalSource] IS NOT NULL");
        });

        modelBuilder.Entity<LaunchCode>(entity =>
        {
            entity.ToTable("LaunchCodes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReturnPath).HasMaxLength(256);
            entity.Property(x => x.Source).HasMaxLength(64);
            entity.HasIndex(x => x.CodeHash).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("PasswordResetTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(x => new { x.RoleId, x.PermissionId });
            entity.HasOne(x => x.Role)
                .WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Permission)
                .WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("UserPermissions");
            entity.HasKey(x => new { x.UserId, x.PermissionId });
            entity.HasOne(x => x.User)
                .WithMany(x => x.UserPermissions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Permission)
                .WithMany(x => x.UserPermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.ToTable("ActivityLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Level).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ActorUserName).HasMaxLength(64);
            entity.Property(x => x.TargetUserName).HasMaxLength(64);
            entity.Property(x => x.EntityType).HasMaxLength(32);
            entity.Property(x => x.EntityName).HasMaxLength(200);
            entity.Property(x => x.HttpMethod).HasMaxLength(16);
            entity.Property(x => x.Path).HasMaxLength(256);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.Property(x => x.Details).HasMaxLength(4000);
            entity.HasIndex(x => x.OccurredAt).IsDescending();
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.Level);
            entity.HasIndex(x => x.ActorUserId);
        });

        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ApplicationName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ApplicationNameArabic).HasMaxLength(80).IsRequired();
            entity.Property(x => x.StudioName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.StudioNameArabic).HasMaxLength(80).IsRequired();
            entity.Property(x => x.OrganizationName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.OrganizationNameArabic).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DefaultLanguage).HasMaxLength(8).IsRequired();
            entity.Property(x => x.LogoContentType).HasMaxLength(64);
            entity.Property(x => x.Logo).HasColumnType("varbinary(max)");
        });

        modelBuilder.Entity<CacheSettings>(entity =>
        {
            entity.ToTable("CacheSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.DefaultProvider).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProvidersJson).IsRequired();
        });

        modelBuilder.Entity<SmtpProfile>(entity =>
        {
            entity.ToTable("SmtpProfiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Host).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(256);
            entity.Property(x => x.Password).HasMaxLength(256);
            entity.Property(x => x.FromAddress).HasMaxLength(256).IsRequired();
            entity.Property(x => x.FromName).HasMaxLength(200);
            entity.HasIndex(x => x.IsDefault)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<AiConnection>(entity =>
        {
            entity.ToTable("AiConnections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Model).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ApiKey).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.IsEnabled).HasDefaultValue(true);
            entity.HasIndex(x => x.IsDefault)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.ToTable("EmailTemplates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(20_000).IsRequired();
            entity.HasIndex(x => x.IsDefault)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<ReportSchedule>(entity =>
        {
            entity.ToTable("ReportSchedules");
            entity.HasKey(x => x.ReportId);
            entity.Property(x => x.Cadence).HasMaxLength(16).IsRequired();
            entity.Property(x => x.LocalTime).HasMaxLength(5).IsRequired();
            entity.Property(x => x.Format).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(8).IsRequired();
            entity.Property(x => x.ScopeField).HasMaxLength(256);
            entity.Property(x => x.ScopeProperty).HasMaxLength(32);
            entity.HasOne<ReportTemplate>()
                .WithMany()
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SmtpProfile>()
                .WithMany()
                .HasForeignKey(x => x.SmtpProfileId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<EmailTemplate>()
                .WithMany()
                .HasForeignKey(x => x.EmailTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.Enabled, x.NextRunAt });
        });

        modelBuilder.Entity<ReportScheduleRecipient>(entity =>
        {
            entity.ToTable("ReportScheduleRecipients");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasOne<ReportSchedule>()
                .WithMany(x => x.Recipients)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Role>()
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
            entity.HasIndex(x => new { x.ReportId, x.RoleId })
                .IsUnique()
                .HasFilter("[RoleId] IS NOT NULL");
            entity.HasIndex(x => new { x.ReportId, x.Email })
                .IsUnique()
                .HasFilter("[UserId] IS NULL AND [RoleId] IS NULL");
        });

        modelBuilder.Entity<ReportScheduleRun>(entity =>
        {
            entity.ToTable("ReportScheduleRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.HasOne<ReportSchedule>()
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ReportId, x.StartedAt });
        });

        modelBuilder.Entity<ReportRevision>(entity =>
        {
            entity.ToTable("ReportRevisions", table => table.UseSqlOutputClause(false));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.NameArabic).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.DescriptionArabic).HasMaxLength(1000);
            entity.Property(x => x.LayoutJson).IsRequired();
            entity.Property(x => x.AccessMode).HasMaxLength(16).IsRequired().HasDefaultValue(ReportAccessModes.Everyone);
            entity.Property(x => x.AccessJson).IsRequired();
            entity.Property(x => x.AccessRoleJson).IsRequired().HasDefaultValue("[]");
            entity.HasIndex(x => new { x.ReportId, x.Version }).IsUnique();
            entity.HasOne<ReportTemplate>()
                .WithMany()
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.PublishedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
