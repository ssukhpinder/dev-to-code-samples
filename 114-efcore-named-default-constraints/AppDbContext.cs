using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NamedDefaultConstraints;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseNamedDefaultConstraints();

        modelBuilder.Entity<Job>(entity =>
        {
            entity.Property(job => job.Status)
                .HasMaxLength(32)
                .HasDefaultValue("queued");

            entity.Property(job => job.CreatedUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(job => job.RetryCount)
                .HasDefaultValue(0);
        });
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=NamedDefaultsDemo;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        return new AppDbContext(options);
    }
}

public sealed class Job
{
    public int Id { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedUtc { get; set; }

    public int RetryCount { get; set; }
}
