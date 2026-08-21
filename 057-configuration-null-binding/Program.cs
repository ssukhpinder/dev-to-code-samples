using System.Text;
using Microsoft.Extensions.Configuration;

const string ExplicitNullJson = """
    {
      "Worker": {
        "Region": null,
        "RetryCount": null,
        "Endpoints": [null, "https://east.example"],
        "EmptyTargets": []
      }
    }
    """;

const string MissingJson = """
    {
      "Worker": {
        "Endpoints": ["https://only.example"]
      }
    }
    """;

const string ValidJson = """
    {
      "Worker": {
        "Region": "ca-west",
        "RetryCount": 4,
        "Endpoints": ["https://east.example", "https://west.example"],
        "EmptyTargets": []
      }
    }
    """;

var checks = new CheckRunner();

var explicitNull = BindDangerous(ExplicitNullJson);
Console.WriteLine("Unsafe direct binding of explicit nulls:");
Console.WriteLine($"  Region={Display(explicitNull.Region)}");
Console.WriteLine($"  RetryCount={explicitNull.RetryCount}");
Console.WriteLine($"  Endpoints={string.Join(" | ", explicitNull.Endpoints!.Select(Display))}");
Console.WriteLine($"  EmptyTargets.Count={explicitNull.EmptyTargets!.Length}");
Console.WriteLine();

checks.Expect(explicitNull.Region is null, "explicit null overwrites a string initializer");
checks.Expect(explicitNull.RetryCount == 0, "explicit null maps a non-nullable int to default(int)");
checks.Expect(explicitNull.Endpoints is [null, "https://east.example"], "null array elements are preserved");
checks.Expect(explicitNull.EmptyTargets is [], "an empty JSON array binds as an empty array");

var missing = BindDangerous(MissingJson);
checks.Expect(missing.Region == "fallback-region", "a missing string keeps its initializer");
checks.Expect(missing.RetryCount == 3, "a missing int keeps its initializer");
checks.Expect(missing.EmptyTargets is null, "a missing array remains at its initial null state");

var rejectedNull = BindAndValidate(ExplicitNullJson);
checks.Expect(rejectedNull.Value is null, "the validation boundary rejects explicit nulls");
checks.Expect(
    rejectedNull.Errors.SequenceEqual(
    [
        "Worker:Region is present but null or blank.",
        "Worker:RetryCount is present but null.",
        "Worker:Endpoints contains a null or invalid absolute HTTPS URI."
    ]),
    "explicit-null validation errors are complete and ordered");

var rejectedMissing = BindAndValidate(MissingJson);
checks.Expect(rejectedMissing.Value is null, "the validation boundary rejects missing required keys");
checks.Expect(
    rejectedMissing.Errors.SequenceEqual(
    [
        "Worker:Region is missing.",
        "Worker:RetryCount is missing."
    ]),
    "missing-key errors stay distinct from explicit-null errors");

var accepted = BindAndValidate(ValidJson);
checks.Expect(accepted.Errors.Length == 0 && accepted.Value is not null, "valid configuration crosses the boundary");
checks.Expect(
    accepted.Value is { Region: "ca-west", RetryCount: 4 } &&
    accepted.Value.Endpoints.SequenceEqual(["https://east.example", "https://west.example"]),
    "validated values match the fixed fixture");

Console.WriteLine();
Console.WriteLine($"Verified {checks.Passed}/{checks.Total} checks.");
Console.WriteLine(
    $"Accepted: region={accepted.Value!.Region}; retries={accepted.Value.RetryCount}; " +
    $"endpoints={string.Join(",", accepted.Value.Endpoints)}");

static DangerousWorkerOptions BindDangerous(string json)
{
    var section = BuildWorkerSection(json);
    return section.Get<DangerousWorkerOptions>()
        ?? throw new InvalidOperationException("Worker configuration did not bind.");
}

static ValidationResult BindAndValidate(string json)
{
    var section = BuildWorkerSection(json);
    var presentKeys = section
        .GetChildren()
        .Select(child => child.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var draft = section.Get<WorkerOptionsDraft>() ?? new WorkerOptionsDraft();
    var errors = new List<string>();

    ValidateRequiredText("Region", draft.Region, presentKeys, errors);

    if (!presentKeys.Contains("RetryCount"))
    {
        errors.Add("Worker:RetryCount is missing.");
    }
    else if (draft.RetryCount is null)
    {
        errors.Add("Worker:RetryCount is present but null.");
    }
    else if (draft.RetryCount is < 1 or > 10)
    {
        errors.Add("Worker:RetryCount must be between 1 and 10.");
    }

    if (!presentKeys.Contains("Endpoints"))
    {
        errors.Add("Worker:Endpoints is missing.");
    }
    else if (draft.Endpoints is null || draft.Endpoints.Length == 0)
    {
        errors.Add("Worker:Endpoints must contain at least one endpoint.");
    }
    else if (draft.Endpoints.Any(endpoint => !IsAbsoluteHttps(endpoint)))
    {
        errors.Add("Worker:Endpoints contains a null or invalid absolute HTTPS URI.");
    }

    if (errors.Count > 0)
    {
        return new ValidationResult(null, [.. errors]);
    }

    return new ValidationResult(
        new WorkerOptions(
            draft.Region!,
            draft.RetryCount!.Value,
            [.. draft.Endpoints!.Select(endpoint => endpoint!)]),
        []);
}

static IConfigurationSection BuildWorkerSection(string json)
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
    return new ConfigurationBuilder()
        .AddJsonStream(stream)
        .Build()
        .GetRequiredSection("Worker");
}

static void ValidateRequiredText(
    string key,
    string? value,
    IReadOnlySet<string> presentKeys,
    ICollection<string> errors)
{
    if (!presentKeys.Contains(key))
    {
        errors.Add($"Worker:{key} is missing.");
    }
    else if (string.IsNullOrWhiteSpace(value))
    {
        errors.Add($"Worker:{key} is present but null or blank.");
    }
}

static bool IsAbsoluteHttps(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

static string Display(string? value) => value ?? "<null>";

file sealed class DangerousWorkerOptions
{
    public string? Region { get; set; } = "fallback-region";

    public int RetryCount { get; set; } = 3;

    public string?[]? Endpoints { get; set; }

    public string?[]? EmptyTargets { get; set; }
}

file sealed class WorkerOptionsDraft
{
    public string? Region { get; set; }

    public int? RetryCount { get; set; }

    public string?[]? Endpoints { get; set; }
}

file sealed record WorkerOptions(string Region, int RetryCount, string[] Endpoints);

file sealed record ValidationResult(WorkerOptions? Value, string[] Errors);

file sealed class CheckRunner
{
    public int Passed { get; private set; }

    public int Total { get; private set; }

    public void Expect(bool condition, string description)
    {
        Total++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL {Total:00}: {description}");
        }

        Passed++;
        Console.WriteLine($"PASS {Passed:00}: {description}");
    }
}
