using Microsoft.EntityFrameworkCore;

using var context = new ProbeDbContext();
var fieldName = args.Length == 0 ? "Name" : args[0];

// This file is intentionally unsafe. The verification script expects its build
// to fail with EF1003, promoted to an error by UnsafeProbe.csproj.
_ = context.Products.FromSqlRaw(
    "SELECT \"Id\", \"Name\" FROM \"Products\" WHERE \"" + fieldName + "\" IS NULL");

internal sealed class ProbeDbContext : DbContext
{
    public DbSet<ProbeProduct> Products => Set<ProbeProduct>();
}

internal sealed class ProbeProduct
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
