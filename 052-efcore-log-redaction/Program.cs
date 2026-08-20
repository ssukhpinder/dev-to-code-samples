using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

const string FirstRole = "billing-reviewer";
const string SecondRole = "fraud-reviewer";

var defaults = await RunScenarioAsync(sensitiveDataLogging: false);
var sensitive = await RunScenarioAsync(sensitiveDataLogging: true);

var checks = new (string Name, bool Passed)[]
{
    ("default query returns both selected roles", defaults.Names.SequenceEqual(["Asha", "Mateo"])),
    ("default log uses redaction markers", defaults.Log.Contains("IN (?, ?)", StringComparison.Ordinal)),
    ("default log hides the first role", !defaults.Log.Contains(FirstRole, StringComparison.Ordinal)),
    ("default log hides the second role", !defaults.Log.Contains(SecondRole, StringComparison.Ordinal)),
    ("sensitive query returns the same rows", sensitive.Names.SequenceEqual(defaults.Names)),
    ("sensitive log shows the first role", sensitive.Log.Contains(FirstRole, StringComparison.Ordinal)),
    ("sensitive log shows the second role", sensitive.Log.Contains(SecondRole, StringComparison.Ordinal)),
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")}: {check.Name}");
}

Console.WriteLine($"Summary: {checks.Count(check => check.Passed)}/{checks.Length} checks passed");
return checks.All(check => check.Passed) ? 0 : 1;

static async Task<ScenarioResult> RunScenarioAsync(bool sensitiveDataLogging)
{
    var messages = new List<string>();
    var builder = new DbContextOptionsBuilder<UsersContext>()
        .UseSqlite("Data Source=:memory:")
        .LogTo(messages.Add, [RelationalEventId.CommandExecuted], LogLevel.Information);

    if (sensitiveDataLogging)
    {
        builder.EnableSensitiveDataLogging();
    }

    await using var context = new UsersContext(builder.Options);
    await context.Database.OpenConnectionAsync();
    await context.Database.EnsureCreatedAsync();
    context.Users.AddRange(
        new User(1, "Asha", FirstRole),
        new User(2, "Mateo", SecondRole),
        new User(3, "Nora", "support"));
    await context.SaveChangesAsync();
    messages.Clear();

    string[] selectedRoles = [FirstRole, SecondRole];
    var names = await context.Users
        .Where(user => EF.Constant(selectedRoles).Contains(user.Role))
        .OrderBy(user => user.Id)
        .Select(user => user.Name)
        .ToArrayAsync();

    return new ScenarioResult(names, string.Join(Environment.NewLine, messages));
}

internal sealed class UsersContext(DbContextOptions<UsersContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
}

internal sealed record User(int Id, string Name, string Role);

internal sealed record ScenarioResult(string[] Names, string Log);
