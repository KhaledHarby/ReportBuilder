namespace ReportBuilder.Core.Entities;

public class CacheSettings
{
    public const int DefaultId = 1;

    public int Id { get; set; } = DefaultId;
    public string DefaultProvider { get; set; } = Models.CacheProviders.SqlServer;
    public string ProvidersJson { get; set; } = "{}";
    public Guid? ModifiedBy { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
