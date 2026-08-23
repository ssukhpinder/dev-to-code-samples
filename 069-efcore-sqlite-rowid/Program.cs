using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

const int expectedChecks = 10;
var checks = 0;

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<KeyContext>()
    .UseSqlite(connection)
    .Options;

await using var db = new KeyContext(options);
await db.Database.EnsureCreatedAsync();

var autoincrementItems = Enumerable.Range(1, 3)
    .Select(index => new AutoincrementItem { Name = $"item-{index}" })
    .ToArray();
var rowidItems = Enumerable.Range(1, 3)
    .Select(index => new RowidItem { Name = $"item-{index}" })
    .ToArray();

db.AddRange(autoincrementItems);
db.AddRange(rowidItems);
Check(
    autoincrementItems.All(item => item.Id == 0)
        && rowidItems.All(item => item.Id == 0),
    "both models start with unset keys");

await db.SaveChangesAsync();

Check(
    autoincrementItems.Select(item => item.Id).SequenceEqual([1, 2, 3]),
    "AUTOINCREMENT keys are database-generated");
Check(
    rowidItems.Select(item => item.Id).SequenceEqual([1, 2, 3]),
    "ROWID keys are database-generated");

var autoincrementSql = await ReadCreateSqlAsync("AutoincrementItems");
var rowidSql = await ReadCreateSqlAsync("RowidItems");

Check(
    autoincrementSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase),
    "convention emits AUTOINCREMENT");
Check(
    !rowidSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase),
    "configured table omits AUTOINCREMENT");
Check(
    rowidSql.Contains("INTEGER", StringComparison.OrdinalIgnoreCase)
        && rowidSql.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase),
    "ROWID table keeps an INTEGER PRIMARY KEY");

db.Remove(autoincrementItems[2]);
db.Remove(rowidItems[2]);
await db.SaveChangesAsync();

var nextAutoincrement = new AutoincrementItem { Name = "replacement" };
var nextRowid = new RowidItem { Name = "replacement" };
db.AddRange(nextAutoincrement, nextRowid);
await db.SaveChangesAsync();

Check(nextAutoincrement.Id == 4, "AUTOINCREMENT does not reuse deleted key 3");
Check(nextRowid.Id == 3, "ROWID reuses the deleted maximum key");

var sequenceNames = await ReadSequenceNamesAsync();
Check(
    sequenceNames.SequenceEqual(["AutoincrementItems"]),
    "sqlite_sequence tracks only the AUTOINCREMENT table");

var autoincrementIds = await db.AutoincrementItems
    .AsNoTracking()
    .OrderBy(item => item.Id)
    .Select(item => item.Id)
    .ToArrayAsync();
var rowidIds = await db.RowidItems
    .AsNoTracking()
    .OrderBy(item => item.Id)
    .Select(item => item.Id)
    .ToArrayAsync();

Check(
    autoincrementIds.SequenceEqual([1, 2, 4])
        && rowidIds.SequenceEqual([1, 2, 3]),
    "final key sets expose the reuse policy");

Console.WriteLine($"PASS: {checks}/{expectedChecks} checks");

async Task<string> ReadCreateSqlAsync(string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = $name";
    command.Parameters.AddWithValue("$name", tableName);

    return (string?)await command.ExecuteScalarAsync()
        ?? throw new InvalidOperationException($"Missing schema for {tableName}.");
}

async Task<string[]> ReadSequenceNamesAsync()
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT name FROM sqlite_sequence ORDER BY name";
    await using var reader = await command.ExecuteReaderAsync();
    var names = new List<string>();

    while (await reader.ReadAsync())
    {
        names.Add(reader.GetString(0));
    }

    return [.. names];
}

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}

public sealed class KeyContext(DbContextOptions<KeyContext> options)
    : DbContext(options)
{
    public DbSet<AutoincrementItem> AutoincrementItems => Set<AutoincrementItem>();

    public DbSet<RowidItem> RowidItems => Set<RowidItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RowidItem>()
            .Property(item => item.Id)
            .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);
    }
}

public sealed class AutoincrementItem
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

public sealed class RowidItem
{
    public int Id { get; set; }

    public required string Name { get; set; }
}
