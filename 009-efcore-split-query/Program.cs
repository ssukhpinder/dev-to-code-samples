using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Demonstrates the cartesian explosion caused by two sibling collection
// Includes in EF Core, and what AsSplitQuery / projection do about it.
//
// Catalog: 12 products, each with 30 reviews and 8 images.

const int ProductCount = 12;
const int ReviewsPerProduct = 30;
const int ImagesPerProduct = 8;

Seed();

Console.WriteLine($"Catalog: {ProductCount} products x {ReviewsPerProduct} reviews x {ImagesPerProduct} images");
Console.WriteLine($"Entities that actually exist: {ProductCount + ProductCount * ReviewsPerProduct + ProductCount * ImagesPerProduct:N0}");
Console.WriteLine();

var scenarios = new (string Name, Func<CatalogContext, int> Query)[]
{
    ("single-query (default)", SingleQuery),
    ("split-query", SplitQuery),
    ("projection", Projection),
};

var dumpSql = args.Contains("--sql");

foreach (var (name, query) in scenarios)
{
    var sql = InspectSql(query, dumpSql);
    var perf = MeasurePerf(query);

    Console.WriteLine($"[{name}]");
    Console.WriteLine($"  SQL statements : {sql.Statements}");
    Console.WriteLine($"  rows read      : {sql.Rows:N0}");
    Console.WriteLine($"  cells read     : {sql.Cells:N0}");
    Console.WriteLine($"  payload (approx): {sql.Bytes / 1024.0:N0} KB");
    Console.WriteLine($"  entities/objects materialized: {perf.Count:N0}");
    Console.WriteLine($"  median time    : {perf.MedianMs:N1} ms");
    Console.WriteLine($"  allocated      : {perf.AllocBytes / 1024.0:N0} KB");
    Console.WriteLine();
}

if (CatalogContext.EfWarnings.Count > 0)
{
    Console.WriteLine("EF Core warned us all along:");
    var warning = CatalogContext.EfWarnings[0];
    // keep it to the interesting part
    var idx = warning.IndexOf("Compiling a query", StringComparison.Ordinal);
    Console.WriteLine("  " + (idx >= 0 ? warning[idx..] : warning));
}

// --------------------------------------------------------------------------
// the three shapes under test
// --------------------------------------------------------------------------

static int SingleQuery(CatalogContext ctx)
{
    var products = ctx.Products
        .Include(p => p.Reviews)
        .Include(p => p.Images)
        .AsNoTracking()
        .ToList();

    return products.Count + products.Sum(p => p.Reviews.Count + p.Images.Count);
}

static int SplitQuery(CatalogContext ctx)
{
    var products = ctx.Products
        .Include(p => p.Reviews)
        .Include(p => p.Images)
        .AsNoTracking()
        .AsSplitQuery()
        .ToList();

    return products.Count + products.Sum(p => p.Reviews.Count + p.Images.Count);
}

static int Projection(CatalogContext ctx)
{
    var cards = ctx.Products
        .Select(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            ReviewCount = p.Reviews.Count,
            AvgStars = p.Reviews.Average(r => (double?)r.Stars),
            Thumbnail = p.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => i.Url)
                .FirstOrDefault(),
        })
        .ToList();

    return cards.Count;
}

// --------------------------------------------------------------------------
// measurement plumbing
// --------------------------------------------------------------------------

// Capture the exact SQL EF runs, then replay it on a raw SQLite connection to
// count what the database actually hands back: rows, cells, and an approximate
// payload size (text length of every non-null cell).
static (int Statements, long Rows, long Cells, long Bytes) InspectSql(Func<CatalogContext, int> query, bool dumpSql)
{
    var capture = new SqlCaptureInterceptor();
    using (var ctx = new CatalogContext(capture))
    {
        query(ctx);
    }

    if (dumpSql)
    {
        foreach (var sql in capture.Statements)
        {
            Console.WriteLine("  --- SQL ---");
            Console.WriteLine(sql);
        }
    }

    long rows = 0, cells = 0, bytes = 0;
    using var conn = new SqliteConnection(CatalogContext.ConnectionString);
    conn.Open();

    foreach (var sql in capture.Statements)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows++;
            cells += reader.FieldCount;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (!reader.IsDBNull(i))
                {
                    bytes += reader.GetValue(i)?.ToString()?.Length ?? 0;
                }
            }
        }
    }

    return (capture.Statements.Count, rows, cells, bytes);
}

static (double MedianMs, long AllocBytes, int Count) MeasurePerf(Func<CatalogContext, int> query)
{
    // warmup: model building, query compilation, connection pool
    using (var ctx = new CatalogContext())
    {
        query(ctx);
    }

    var times = new List<double>();
    var allocs = new List<long>();
    var count = 0;

    for (var i = 0; i < 9; i++)
    {
        using var ctx = new CatalogContext();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        count = query(ctx);
        sw.Stop();
        allocs.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        times.Add(sw.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    allocs.Sort();
    return (times[times.Count / 2], allocs[allocs.Count / 2], count);
}

// --------------------------------------------------------------------------
// seeding
// --------------------------------------------------------------------------

static void Seed()
{
    using var ctx = new CatalogContext();
    ctx.Database.EnsureDeleted();
    ctx.Database.EnsureCreated();

    string[] snippets =
    {
        "Solid build quality and the battery life is better than advertised.",
        "Took a week to arrive but works exactly as described in the listing.",
        "The mounting bracket feels flimsy; everything else is fine so far.",
        "Replaced the older model with this one and the difference is obvious.",
        "Firmware update fixed the pairing issue I had on day one.",
        "Good value for the price, though the manual is basically useless.",
    };

    var rng = new Random(42);

    for (var p = 1; p <= ProductCount; p++)
    {
        var product = new Product
        {
            Name = $"Gadget {p:D2}",
            Sku = $"GDG-{p:D4}",
            Price = 19.99m + p * 10,
            Description = $"The Gadget {p:D2} is the {p}th iteration of a device nobody can quite explain, but everyone keeps buying.",
        };

        for (var r = 0; r < ReviewsPerProduct; r++)
        {
            product.Reviews.Add(new Review
            {
                Stars = 1 + rng.Next(5),
                Author = $"user{rng.Next(1000, 9999)}",
                Comment = $"{snippets[rng.Next(snippets.Length)]} {snippets[rng.Next(snippets.Length)]} ({r + 1}/{ReviewsPerProduct})",
                PostedAt = new DateTime(2026, 1, 1).AddDays(rng.Next(180)),
            });
        }

        for (var i = 0; i < ImagesPerProduct; i++)
        {
            product.Images.Add(new ProductImage
            {
                Url = $"https://cdn.example.com/products/gdg-{p:D4}/image-{i}.webp",
                AltText = $"Gadget {p:D2}, angle {i + 1}",
                SortOrder = i,
            });
        }

        ctx.Products.Add(product);
    }

    ctx.SaveChanges();
}

// --------------------------------------------------------------------------
// EF Core bits
// --------------------------------------------------------------------------

sealed class SqlCaptureInterceptor : DbCommandInterceptor
{
    public List<string> Statements { get; } = [];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Statements.Add(command.CommandText);
        return result;
    }
}

sealed class CatalogContext : DbContext
{
    public const string ConnectionString = "Data Source=catalog.db";

    private readonly SqlCaptureInterceptor? _capture;

    public CatalogContext(SqlCaptureInterceptor? capture = null) => _capture = capture;

    public static List<string> EfWarnings { get; } = [];

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(ConnectionString);
        options.LogTo(
            EfWarnings.Add,
            [RelationalEventId.MultipleCollectionIncludeWarning]);

        if (_capture is not null)
        {
            options.AddInterceptors(_capture);
        }
    }
}

sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Sku { get; set; }
    public decimal Price { get; set; }
    public required string Description { get; set; }
    public List<Review> Reviews { get; } = [];
    public List<ProductImage> Images { get; } = [];
}

sealed class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Stars { get; set; }
    public required string Author { get; set; }
    public required string Comment { get; set; }
    public DateTime PostedAt { get; set; }
}

sealed class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string Url { get; set; }
    public required string AltText { get; set; }
    public int SortOrder { get; set; }
}
