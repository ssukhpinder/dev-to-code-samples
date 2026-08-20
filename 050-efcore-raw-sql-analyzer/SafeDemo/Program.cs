using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseSqlite(connection)
    .Options;

await using var context = new CatalogDbContext(options);
await context.Database.EnsureCreatedAsync();

context.Products.AddRange(
    new Product(1, "Plain mug", "Kitchen"),
    new Product(2, "Travel mug", "Kitchen"),
    new Product(3, "Grid notebook", "Office"));
await context.SaveChangesAsync();

var maliciousLookingValue = "mug' OR 1=1 --";
var parameterizedQuery = context.Products
    .FromSql($"""
        SELECT "Id", "Name", "Category"
        FROM "Products"
        WHERE "Name" = {maliciousLookingValue}
        """);

var parameterizedSql = parameterizedQuery.ToQueryString();
var parameterizedRows = await parameterizedQuery.ToArrayAsync();
var allowlistedRows = await QueryByField(context, ProductField.Category, "Kitchen")
    .OrderBy(product => product.Id)
    .ToArrayAsync();

var checks = new[]
{
    new Verification(
        "FromSql keeps the value in a parameter",
        parameterizedSql.Contains("@p0", StringComparison.Ordinal)),
    new Verification(
        "malicious-looking input matches no rows",
        parameterizedRows.Length == 0),
    new Verification(
        "the injected OR clause never changes the result set",
        await context.Products.CountAsync() == 3),
    new Verification(
        "the allowlisted category template returns both kitchen products",
        allowlistedRows.Select(product => product.Name).SequenceEqual(["Plain mug", "Travel mug"])),
    new Verification(
        "the allowlisted raw template still parameterizes its value",
        QueryByField(context, ProductField.Category, "Kitchen")
            .ToQueryString()
            .Contains("@p0", StringComparison.Ordinal)),
    new Verification(
        "an unsupported identifier cannot reach a raw SQL template",
        RejectsUnsupportedField(context)),
};

var passed = 0;
foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
    passed += check.Passed ? 1 : 0;
}

Console.WriteLine($"Verifier: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static IQueryable<Product> QueryByField(
    CatalogDbContext context,
    ProductField field,
    string value)
{
    // Identifiers cannot be parameters. Select a complete, hard-coded SQL template;
    // pass only the value through FromSqlRaw's parameter argument.
    var sql = field switch
    {
        ProductField.Name => """
            SELECT "Id", "Name", "Category"
            FROM "Products"
            WHERE "Name" = {0}
            """,
        ProductField.Category => """
            SELECT "Id", "Name", "Category"
            FROM "Products"
            WHERE "Category" = {0}
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported product field."),
    };

    return context.Products.FromSqlRaw(sql, value);
}

static bool RejectsUnsupportedField(CatalogDbContext context)
{
    try
    {
        _ = QueryByField(context, (ProductField)999, "anything");
        return false;
    }
    catch (ArgumentOutOfRangeException)
    {
        return true;
    }
}

internal enum ProductField
{
    Name,
    Category,
}

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

internal sealed class Product(int id, string name, string category)
{
    public int Id { get; init; } = id;

    public string Name { get; init; } = name;

    public string Category { get; init; } = category;
}

internal sealed record Verification(string Name, bool Passed);
