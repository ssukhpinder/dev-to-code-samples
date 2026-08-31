using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NamedDefaultConstraints;

const int expectedChecks = 9;
var checks = 0;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=offline.invalid;Database=NamedDefaultsDemo;Integrated Security=True;TrustServerCertificate=True")
    .Options;

await using var db = new AppDbContext(options);
var migrationsAssembly = db.GetService<IMigrationsAssembly>();
var migrationIds = migrationsAssembly.Migrations.Keys.Order(StringComparer.Ordinal).ToArray();

Check(
    migrationIds.Length == 2
        && migrationIds[0].EndsWith("_InitialSchema", StringComparison.Ordinal)
        && migrationIds[1].EndsWith("_NameDefaultConstraints", StringComparison.Ordinal),
    "sample contains the baseline and naming migrations");

var namingMigration = migrationsAssembly.CreateMigration(
    migrationsAssembly.Migrations[migrationIds[1]],
    db.Database.ProviderName!);
var alteredDefaults = namingMigration.UpOperations
    .OfType<AlterColumnOperation>()
    .ToArray();

Check(alteredDefaults.Length == 3, "enabling the convention changes all three defaults");

var annotatedNames = alteredDefaults
    .Select(operation => operation[RelationalAnnotationNames.DefaultConstraintName] as string)
    .Order(StringComparer.Ordinal)
    .ToArray();
var expectedNames = new[]
{
    "DF_Jobs_CreatedUtc",
    "DF_Jobs_RetryCount",
    "DF_Jobs_Status",
};

Check(
    annotatedNames.SequenceEqual(expectedNames, StringComparer.Ordinal),
    "the migration records deterministic constraint names");

var sql = GenerateScript(options, migrationIds[0], migrationIds[1]);
var repeatedSql = GenerateScript(options, migrationIds[0], migrationIds[1]);

Check(sql == repeatedSql, "migration SQL is deterministic across repeated generation");
Check(
    CountOccurrences(sql, "FROM [sys].[default_constraints]") == 3,
    "SQL discovers each database-generated constraint name");
Check(
    CountOccurrences(sql, "DROP CONSTRAINT") == 3,
    "SQL drops all three existing default constraints");
Check(
    expectedNames.All(name => sql.Contains($"ADD CONSTRAINT [{name}]", StringComparison.Ordinal)),
    "SQL recreates all three defaults with predictable names");
Check(
    !sql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase)
        && !sql.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase),
    "preview contains no table or column drop");
Check(
    db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed,
    "placeholder SQL Server connection remains closed");

Console.WriteLine($"PASS: {checks}/{expectedChecks} checks");

static int CountOccurrences(string value, string token)
{
    var count = 0;
    var offset = 0;

    while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += token.Length;
    }

    return count;
}

static string GenerateScript(
    DbContextOptions<AppDbContext> options,
    string fromMigration,
    string toMigration)
{
    using var context = new AppDbContext(options);
    return context.GetService<IMigrator>().GenerateScript(fromMigration, toMigration);
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
