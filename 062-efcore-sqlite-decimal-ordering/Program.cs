using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

const int expectedChecks = 10;
var checks = 0;
decimal[] expectedAscending = [-1.25m, 2.50m, 9.75m, 10.00m, 100.25m];

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<PriceContext>()
    .UseSqlite(connection)
    .Options;

await using var db = new PriceContext(options);
await db.Database.EnsureCreatedAsync();

db.Products.AddRange(
    new Product { Name = "Budget", Price = -1.25m },
    new Product { Name = "Starter", Price = 2.50m },
    new Product { Name = "Standard", Price = 9.75m },
    new Product { Name = "Plus", Price = 10.00m },
    new Product { Name = "Enterprise", Price = 100.25m });
await db.SaveChangesAsync();

Check(await db.Products.CountAsync() == 5, "seeded five fixed prices");

await using (var storageCommand = connection.CreateCommand())
{
    storageCommand.CommandText =
        "SELECT COUNT(*) FROM Products WHERE typeof(Price) = 'text'";
    var textCount = Convert.ToInt32(
        await storageCommand.ExecuteScalarAsync(),
        CultureInfo.InvariantCulture);

    Check(textCount == 5, "SQLite stores every decimal as TEXT");
}

var rawOrder = new List<decimal>();
await using (var rawOrderCommand = connection.CreateCommand())
{
    rawOrderCommand.CommandText = "SELECT Price FROM Products ORDER BY Price";
    await using var reader = await rawOrderCommand.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        rawOrder.Add(decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture));
    }
}

Check(
    !rawOrder.SequenceEqual(expectedAscending),
    "plain SQLite TEXT ordering is not numeric ordering");

var ascendingQuery = db.Products
    .AsNoTracking()
    .OrderBy(product => product.Price)
    .Select(product => product.Price);
var ascendingSql = ascendingQuery.ToQueryString();

Check(
    ascendingSql.Contains("COLLATE EF_DECIMAL", StringComparison.OrdinalIgnoreCase),
    "EF query uses the decimal collation");

var ascending = await ascendingQuery.ToArrayAsync();
Check(ascending.SequenceEqual(expectedAscending), "ascending prices are numeric");

var descending = await db.Products
    .AsNoTracking()
    .OrderByDescending(product => product.Price)
    .Select(product => product.Price)
    .ToArrayAsync();
Check(
    descending.SequenceEqual(expectedAscending.Reverse()),
    "descending prices are numeric");

var aggregateQuery = db.Products
    .AsNoTracking()
    .GroupBy(_ => 1)
    .Select(group => new
    {
        Minimum = group.Min(product => product.Price),
        Maximum = group.Max(product => product.Price)
    });
var aggregateSql = aggregateQuery.ToQueryString();

Check(
    aggregateSql.Contains("ef_min", StringComparison.OrdinalIgnoreCase),
    "minimum stays in SQL");
Check(
    aggregateSql.Contains("ef_max", StringComparison.OrdinalIgnoreCase),
    "maximum stays in SQL");

var range = await aggregateQuery.SingleAsync();
Check(range.Minimum == -1.25m, "minimum price is correct");
Check(range.Maximum == 100.25m, "maximum price is correct");

Console.WriteLine($"PASS: {checks}/{expectedChecks} checks");

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}

public sealed class PriceContext(DbContextOptions<PriceContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

public sealed class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }
}
