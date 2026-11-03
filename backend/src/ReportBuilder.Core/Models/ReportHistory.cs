namespace ReportBuilder.Core.Models;

public sealed class ReportHistory
{
    public int CurrentVersion { get; set; }
    public ReportLayoutDiff? DraftVersusPublished { get; set; }
    public IReadOnlyList<ReportRevisionItem> Revisions { get; set; } = [];
}

public sealed class ReportRevisionItem
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameArabic { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string? PublishedByName { get; set; }
    public bool IsCurrent { get; set; }
    public IReadOnlyList<string> OnlyHere { get; set; } = [];
    public IReadOnlyList<string> OnlyInPublished { get; set; } = [];
    public bool FiltersChanged { get; set; }
    public bool SortChanged { get; set; }
    public bool NameChanged { get; set; }
}

public sealed class ReportLayoutDiff
{
    public IReadOnlyList<string> OnlyHere { get; set; } = [];
    public IReadOnlyList<string> OnlyInPublished { get; set; } = [];
    public bool FiltersChanged { get; set; }
    public bool SortChanged { get; set; }
    public bool NameChanged { get; set; }
}
