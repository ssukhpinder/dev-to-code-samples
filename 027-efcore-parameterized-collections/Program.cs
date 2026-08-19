using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

using var db = new CatalogContext();

int passed = 0;

var ids = new[] { 3, 5, 8 };
var defaultSql = db.Products
    .Where(product => ids.Contains(product.Id))
    .ToQueryString();

Verify(
    "EF Core 10 default uses multiple scalar parameters",
    !defaultSql.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase)
        && CountSqlParameters(defaultSql) == 3);

var singleParameterSql = db.Products
    .Where(product => EF.Parameter(ids).Contains(product.Id))
    .ToQueryString();

Verify(
    "EF.Parameter restores one JSON collection parameter",
    singleParameterSql.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase)
        && CountSqlParameters(singleParameterSql) == 1);

var constantSql = db.Products
    .Where(product => EF.Constant(ids).Contains(product.Id))
    .ToQueryString();

Verify(
    "EF.Constant inlines this collection",
    constantSql.Contains("IN (3, 5, 8)", StringComparison.Ordinal)
        && CountSqlParameters(constantSql) == 0);

var explicitMultipleSql = db.Products
    .Where(product => EF.MultipleParameters(ids).Contains(product.Id))
    .ToQueryString();

Verify(
    "EF.MultipleParameters makes the scalar strategy explicit",
    !explicitMultipleSql.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase)
        && CountSqlParameters(explicitMultipleSql) == 3);

var eightIds = Enumerable.Range(1, 8).ToArray();
var paddedSql = db.Products
    .Where(product => eightIds.Contains(product.Id))
    .ToQueryString();

Verify(
    "Eight values are padded to a ten-parameter SQL shape",
    CountSqlParameters(paddedSql) == 10);

Show("Default", defaultSql);
Show("EF.Parameter", singleParameterSql);
Show("EF.Constant", constantSql);
Show("Eight-value default", paddedSql);

Console.WriteLine($"All checks passed: {passed}/5");

void Verify(string name, bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {name}");
    }

    passed++;
    Console.WriteLine($"PASS: {name}");
}

static int CountSqlParameters(string sql) =>
    Regex.Matches(sql, "@[A-Za-z_][A-Za-z0-9_]*")
        .Select(match => match.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

static void Show(string name, string sql)
{
    Console.WriteLine();
    Console.WriteLine($"--- {name} ---");
    Console.WriteLine(sql);
}

internal sealed class CatalogContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=SqlShapeOnly;Integrated Security=true;TrustServerCertificate=true");
}

internal sealed class Product
{
    public int Id { get; init; }

    public required string Name { get; init; }
}
