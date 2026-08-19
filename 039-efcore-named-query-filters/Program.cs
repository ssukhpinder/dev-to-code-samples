using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<TenantDbContext>()
    .UseSqlite(connection)
    .Options;

await using (var seedContext = new TenantDbContext(options, TenantIds.North))
{
    await seedContext.Database.EnsureCreatedAsync();
    seedContext.WorkItems.AddRange(
        new WorkItem(1, TenantIds.North, "north-active", isDeleted: false),
        new WorkItem(2, TenantIds.North, "north-deleted", isDeleted: true),
        new WorkItem(3, TenantIds.South, "south-active", isDeleted: false),
        new WorkItem(4, TenantIds.South, "south-deleted", isDeleted: true));
    await seedContext.SaveChangesAsync();
}

await using var northContext = new TenantDbContext(options, TenantIds.North);
await using var southContext = new TenantDbContext(options, TenantIds.South);

var checks = new[]
{
    new Verification(
        "default query keeps both filters",
        await TitlesAsync(northContext.WorkItems),
        ["north-active"]),
    new Verification(
        "recycle bin disables only soft delete",
        await TitlesAsync(northContext.WorkItems.IgnoreQueryFilters(
            [TenantDbContext.SoftDeletionFilter])),
        ["north-active", "north-deleted"]),
    new Verification(
        "disabling only tenant filter still hides deleted rows",
        await TitlesAsync(northContext.WorkItems.IgnoreQueryFilters(
            [TenantDbContext.TenantFilter])),
        ["north-active", "south-active"]),
    new Verification(
        "parameterless IgnoreQueryFilters disables every boundary",
        await TitlesAsync(northContext.WorkItems.IgnoreQueryFilters()),
        ["north-active", "north-deleted", "south-active", "south-deleted"]),
    new Verification(
        "tenant value is scoped per DbContext",
        await TitlesAsync(southContext.WorkItems),
        ["south-active"]),
};

var passed = 0;
foreach (var check in checks)
{
    var success = check.Actual.SequenceEqual(check.Expected, StringComparer.Ordinal);
    Console.WriteLine(
        $"{(success ? "PASS" : "FAIL")} {check.Name}: [{string.Join(", ", check.Actual)}]");
    passed += success ? 1 : 0;
}

Console.WriteLine($"Verifier: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static async Task<string[]> TitlesAsync(IQueryable<WorkItem> query) =>
    await query
        .OrderBy(item => item.Title)
        .Select(item => item.Title)
        .ToArrayAsync();

internal sealed class TenantDbContext(
    DbContextOptions<TenantDbContext> options,
    string tenantId) : DbContext(options)
{
    public const string SoftDeletionFilter = "SoftDeletionFilter";
    public const string TenantFilter = "TenantFilter";

    private readonly string _tenantId = tenantId;

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkItem>()
            .HasQueryFilter(SoftDeletionFilter, item => !item.IsDeleted)
            .HasQueryFilter(TenantFilter, item => item.TenantId == _tenantId);
    }
}

internal sealed class WorkItem(
    int id,
    string tenantId,
    string title,
    bool isDeleted)
{
    public int Id { get; init; } = id;

    public string TenantId { get; init; } = tenantId;

    public string Title { get; init; } = title;

    public bool IsDeleted { get; init; } = isDeleted;
}

internal sealed record Verification(string Name, string[] Actual, string[] Expected);

internal static class TenantIds
{
    public const string North = "north";
    public const string South = "south";
}
