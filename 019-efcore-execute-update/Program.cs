// ExecuteUpdate vs load-modify-save: what a set-based UPDATE actually saves.
// Scenario: a nightly job archives every delivered order older than 90 days.
//
//   Strategy A (the default habit): query the entities, flip the flag, SaveChanges.
//   Strategy B: ctx.Orders.Where(...).ExecuteUpdate(...)  -- one SQL statement.
//
// A DbCommandInterceptor counts every command EF sends so the round trips are
// measured, not guessed. Timings are the median of 5 rounds after a warmup
// round per strategy. Allocations via GC.GetAllocatedBytesForCurrentThread.

using System.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

const int TotalOrders = 20_000;
const int Rounds = 5;
var cutoffUtc = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc).AddDays(-90);

var dbPath = Path.Combine(AppContext.BaseDirectory, "orders.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

var counter = new CommandCounter();

// ---------------------------------------------------------------- seed
using (var ctx = new ShopContext(dbPath, counter))
{
    ctx.Database.EnsureCreated();

    var rng = new Random(42); // deterministic seed => same counts every run
    var statuses = new[]
    {
        OrderStatus.Delivered, OrderStatus.Delivered, OrderStatus.Delivered,
        OrderStatus.Pending, OrderStatus.Cancelled
    };
    var seedBase = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc);

    var orders = new List<Order>(TotalOrders);
    for (var i = 0; i < TotalOrders; i++)
    {
        orders.Add(new Order
        {
            CustomerEmail = $"customer{i % 3_000}@example.com",
            Status = statuses[rng.Next(statuses.Length)],
            PlacedAtUtc = seedBase.AddDays(-rng.Next(1, 366)),
            Total = Math.Round((decimal)(rng.NextDouble() * 480 + 20), 2),
            IsArchived = false,
        });
    }
    ctx.Orders.AddRange(orders);
    ctx.SaveChanges();

    var matching = ctx.Orders.Count(o => o.Status == OrderStatus.Delivered && o.PlacedAtUtc < cutoffUtc);
    Console.WriteLine($"seeded {TotalOrders:N0} orders, {matching:N0} match the archive predicate\n");
}

// ---------------------------------------------------------------- strategy A: load, modify, SaveChanges
var loadStats = Measure("A: load entities + SaveChanges", counter, dbPath, ctx =>
{
    var stale = ctx.Orders
        .Where(o => o.Status == OrderStatus.Delivered && o.PlacedAtUtc < cutoffUtc)
        .ToList();

    foreach (var order in stale)
        order.IsArchived = true;

    ctx.SaveChanges();
    return stale.Count;
});

// ---------------------------------------------------------------- strategy B: ExecuteUpdate
var setStats = Measure("B: ExecuteUpdate", counter, dbPath, ctx =>
    ctx.Orders
        .Where(o => o.Status == OrderStatus.Delivered && o.PlacedAtUtc < cutoffUtc)
        .ExecuteUpdate(s => s.SetProperty(o => o.IsArchived, true)));

Console.WriteLine("== archive job, median of 5 rounds ==");
Print(loadStats);
Print(setStats);

Console.WriteLine("\nExecuteUpdate SQL as EF generated it:");
Console.WriteLine(counter.LastUpdateSql + "\n");

// ---------------------------------------------------------------- the stale-tracker gotcha
using (var ctx = new ShopContext(dbPath, counter))
{
    ResetArchivedFlags(dbPath);

    var tracked = ctx.Orders.First(o => o.Status == OrderStatus.Delivered && o.PlacedAtUtc < cutoffUtc);

    ctx.Orders
        .Where(o => o.Status == OrderStatus.Delivered && o.PlacedAtUtc < cutoffUtc)
        .ExecuteUpdate(s => s.SetProperty(o => o.IsArchived, true));

    var fresh = ctx.Orders.AsNoTracking().First(o => o.Id == tracked.Id);

    Console.WriteLine("== the stale-tracker gotcha ==");
    Console.WriteLine($"  tracked entity says IsArchived = {tracked.IsArchived}");
    Console.WriteLine($"  database says       IsArchived = {fresh.IsArchived}\n");
}

// ---------------------------------------------------------------- cameo: ExecuteDelete
using (var ctx = new ShopContext(dbPath, counter))
{
    counter.Reset();
    var sw = Stopwatch.StartNew();
    var purged = ctx.Orders
        .Where(o => o.Status == OrderStatus.Cancelled && o.PlacedAtUtc < cutoffUtc.AddDays(-90))
        .ExecuteDelete();
    sw.Stop();
    Console.WriteLine("== cameo: ExecuteDelete ==");
    Console.WriteLine($"  purged {purged:N0} cancelled orders in {sw.Elapsed.TotalMilliseconds:F1} ms, {counter.Commands} SQL command(s)");
}

return;

// ---------------------------------------------------------------- helpers

static Stats Measure(string label, CommandCounter counter, string dbPath, Func<ShopContext, int> run)
{
    var times = new List<double>();
    long commands = 0, rows = 0, allocated = 0;

    for (var round = 0; round <= Rounds; round++) // round 0 = warmup
    {
        ResetArchivedFlags(dbPath);
        using var ctx = new ShopContext(dbPath, counter);
        counter.Reset();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var affected = run(ctx);
        sw.Stop();
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;

        if (round == 0) continue; // warmup: model building + query compilation
        times.Add(sw.Elapsed.TotalMilliseconds);
        commands = counter.Commands;
        rows = affected;
        allocated = alloc;
    }

    times.Sort();
    return new Stats(label, rows, commands, times[times.Count / 2], allocated);
}

static void ResetArchivedFlags(string dbPath)
{
    using var ctx = new ShopContext(dbPath, counter: null);
    ctx.Database.ExecuteSqlRaw("UPDATE Orders SET IsArchived = 0");
}

static void Print(Stats s)
{
    Console.WriteLine($"  [{s.Label}]");
    Console.WriteLine($"    rows touched   : {s.Rows:N0}");
    Console.WriteLine($"    SQL commands   : {s.Commands:N0}");
    Console.WriteLine($"    median time    : {s.MedianMs:F1} ms");
    Console.WriteLine($"    allocated      : {s.AllocatedBytes / 1024.0:N0} KB");
}

record Stats(string Label, long Rows, long Commands, double MedianMs, long AllocatedBytes);

enum OrderStatus { Pending, Delivered, Cancelled }

class Order
{
    public int Id { get; set; }
    public required string CustomerEmail { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public decimal Total { get; set; }
    public bool IsArchived { get; set; }
}

class ShopContext(string dbPath, CommandCounter? counter) : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={dbPath}");
        if (counter is not null) options.AddInterceptors(counter);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasIndex(o => new { o.Status, o.PlacedAtUtc });
            e.Property(o => o.Total).HasConversion<double>();
        });
    }
}

// Counts every command EF executes and keeps the last UPDATE statement it saw.
class CommandCounter : DbCommandInterceptor
{
    private long _commands;
    public long Commands => Interlocked.Read(ref _commands);
    public string LastUpdateSql { get; private set; } = "";

    public void Reset() => Interlocked.Exchange(ref _commands, 0);

    private void Count(DbCommand command)
    {
        Interlocked.Increment(ref _commands);
        if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            LastUpdateSql = command.CommandText;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Count(command); return result; }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Count(command); return result; }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    { Count(command); return result; }
}
