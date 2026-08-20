using System.Diagnostics;
using System.Diagnostics.Metrics;

return TelemetrySchemaSample.Run();

internal static class TelemetrySchemaSample
{
    private const string InstrumentationName = "Example.Checkout";
    private const string InstrumentationVersion = "2.0.0";
    private const string SchemaVersion = "v2.0.0";
    private const string CurrentSchemaUrl =
        "https://schemas.example.com/checkout/v2.0.0/telemetry.json";
    private const string StaleSchemaUrl =
        "https://schemas.example.com/checkout/v1.4.0/telemetry.json";

    public static int Run()
    {
        var checks = new CheckRunner();
        var stoppedActivities = new List<ActivitySnapshot>();
        var measurements = new List<MeasurementSnapshot>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == InstrumentationName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Add(ActivitySnapshot.Capture(activity))
        };
        ActivitySource.AddActivityListener(activityListener);

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == InstrumentationName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
                measurements.Add(MeasurementSnapshot.Capture(instrument, measurement, tags)));
        meterListener.Start();

        using var activitySource = CreateActivitySource(CurrentSchemaUrl);
        using var meter = CreateMeter(CurrentSchemaUrl);
        Counter<long> validationCounter = meter.CreateCounter<long>(
            "checkout.validations",
            unit: "{validation}",
            description: "Checkout validation decisions.");

        var contract = new TelemetryContract(
            InstrumentationName,
            InstrumentationVersion,
            SchemaVersion,
            new Uri(CurrentSchemaUrl, UriKind.Absolute));

        ContractCheck currentCheck = TelemetryContractGuard.Validate(activitySource, meter, contract);
        checks.Expect(
            currentCheck.IsValid,
            "matching trace and metric metadata satisfies the contract");
        checks.Expect(
            activitySource.TelemetrySchemaUrl == CurrentSchemaUrl &&
            meter.TelemetrySchemaUrl == CurrentSchemaUrl &&
            CurrentSchemaUrl.StartsWith("https://", StringComparison.Ordinal) &&
            CurrentSchemaUrl.Contains($"/{SchemaVersion}/", StringComparison.Ordinal),
            "both signals expose the expected versioned HTTPS schema URL");

        using var staleMeter = CreateMeter(StaleSchemaUrl);
        ContractCheck staleCheck = TelemetryContractGuard.Validate(activitySource, staleMeter, contract);
        checks.Expect(
            !staleCheck.IsValid &&
            staleCheck.Errors.Any(error => error.StartsWith("Metric schema URL", StringComparison.Ordinal)),
            "the offline guard rejects a stale metric schema URL");
        checks.Expect(
            staleCheck.Errors.Contains("Trace and metric schema URLs differ."),
            "the offline guard reports cross-signal schema drift");

        using (Activity? activity = activitySource.StartActivity(
            "checkout.validate",
            ActivityKind.Internal))
        {
            if (activity is not null)
            {
                activity.SetTag("checkout.id", "order-42");
                activity.SetTag("checkout.result", "accepted");
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            var metricTags = new TagList
            {
                { "checkout.id", "order-42" },
                { "checkout.result", "accepted" }
            };
            validationCounter.Add(1, metricTags);
        }

        checks.Expect(
            stoppedActivities.Count == 1,
            "ActivityListener captures exactly one completed activity");
        checks.Expect(
            stoppedActivities.Count == 1 &&
            stoppedActivities[0].OperationName == "checkout.validate" &&
            stoppedActivities[0].Status == ActivityStatusCode.Ok &&
            HasTag(stoppedActivities[0].Tags, "checkout.id", "order-42") &&
            HasTag(stoppedActivities[0].Tags, "checkout.result", "accepted"),
            "the activity has the deterministic operation, status, and tags");
        checks.Expect(
            stoppedActivities.Count == 1 &&
            stoppedActivities[0].SourceName == InstrumentationName &&
            stoppedActivities[0].SourceVersion == InstrumentationVersion &&
            stoppedActivities[0].SchemaUrl == CurrentSchemaUrl,
            "the trace listener observes the source schema metadata");
        checks.Expect(
            measurements.Count == 1,
            "MeterListener captures exactly one counter measurement");
        checks.Expect(
            measurements.Count == 1 &&
            measurements[0].InstrumentName == "checkout.validations" &&
            measurements[0].Unit == "{validation}" &&
            measurements[0].Value == 1 &&
            HasTag(measurements[0].Tags, "checkout.id", "order-42") &&
            HasTag(measurements[0].Tags, "checkout.result", "accepted"),
            "the metric has the deterministic value, unit, and tags");
        checks.Expect(
            measurements.Count == 1 &&
            measurements[0].MeterName == InstrumentationName &&
            measurements[0].MeterVersion == InstrumentationVersion &&
            measurements[0].SchemaUrl == CurrentSchemaUrl,
            "the metric listener observes the meter schema metadata");

        return checks.Finish();
    }

    private static ActivitySource CreateActivitySource(string schemaUrl) =>
        new(new ActivitySourceOptions(InstrumentationName)
        {
            Version = InstrumentationVersion,
            TelemetrySchemaUrl = schemaUrl,
            Tags = [new("component", "checkout")]
        });

    private static Meter CreateMeter(string schemaUrl) =>
        new(new MeterOptions(InstrumentationName)
        {
            Version = InstrumentationVersion,
            TelemetrySchemaUrl = schemaUrl,
            Tags = [new("component", "checkout")]
        });

    private static bool HasTag(
        IReadOnlyDictionary<string, object?> tags,
        string key,
        object expected) =>
        tags.TryGetValue(key, out object? actual) && Equals(actual, expected);
}

internal sealed record TelemetryContract(
    string InstrumentationName,
    string InstrumentationVersion,
    string SchemaVersion,
    Uri SchemaUrl);

internal sealed record ContractCheck(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static class TelemetryContractGuard
{
    public static ContractCheck Validate(
        ActivitySource activitySource,
        Meter meter,
        TelemetryContract expected)
    {
        var errors = new List<string>();

        Compare("Trace source name", activitySource.Name, expected.InstrumentationName, errors);
        Compare("Metric meter name", meter.Name, expected.InstrumentationName, errors);
        Compare("Trace source version", activitySource.Version, expected.InstrumentationVersion, errors);
        Compare("Metric meter version", meter.Version, expected.InstrumentationVersion, errors);

        ValidateSchemaUrl("Trace", activitySource.TelemetrySchemaUrl, expected, errors);
        ValidateSchemaUrl("Metric", meter.TelemetrySchemaUrl, expected, errors);

        if (!string.Equals(
            activitySource.TelemetrySchemaUrl,
            meter.TelemetrySchemaUrl,
            StringComparison.Ordinal))
        {
            errors.Add("Trace and metric schema URLs differ.");
        }

        return new ContractCheck(errors);
    }

    private static void Compare(
        string field,
        string? actual,
        string expected,
        ICollection<string> errors)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{field} '{actual ?? "<null>"}' does not match '{expected}'.");
        }
    }

    private static void ValidateSchemaUrl(
        string signal,
        string? actual,
        TelemetryContract expected,
        ICollection<string> errors)
    {
        if (!Uri.TryCreate(actual, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{signal} schema URL must be an absolute HTTPS URL.");
            return;
        }

        if (uri != expected.SchemaUrl)
        {
            errors.Add(
                $"{signal} schema URL '{uri.AbsoluteUri}' does not match " +
                $"'{expected.SchemaUrl.AbsoluteUri}'.");
        }

        string versionSegment = $"/{expected.SchemaVersion}/";
        if (!uri.AbsolutePath.Contains(versionSegment, StringComparison.Ordinal))
        {
            errors.Add(
                $"{signal} schema URL does not contain version segment '{versionSegment}'.");
        }
    }
}

internal sealed record ActivitySnapshot(
    string OperationName,
    ActivityStatusCode Status,
    string SourceName,
    string? SourceVersion,
    string? SchemaUrl,
    IReadOnlyDictionary<string, object?> Tags)
{
    public static ActivitySnapshot Capture(Activity activity) =>
        new(
            activity.OperationName,
            activity.Status,
            activity.Source.Name,
            activity.Source.Version,
            activity.Source.TelemetrySchemaUrl,
            activity.TagObjects.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal));
}

internal sealed record MeasurementSnapshot(
    string InstrumentName,
    string? Unit,
    long Value,
    string MeterName,
    string? MeterVersion,
    string? SchemaUrl,
    IReadOnlyDictionary<string, object?> Tags)
{
    public static MeasurementSnapshot Capture(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copiedTags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            copiedTags.Add(tag.Key, tag.Value);
        }

        return new MeasurementSnapshot(
            instrument.Name,
            instrument.Unit,
            measurement,
            instrument.Meter.Name,
            instrument.Meter.Version,
            instrument.Meter.TelemetrySchemaUrl,
            copiedTags);
    }
}

internal sealed class CheckRunner
{
    private readonly List<string> _failures = [];
    private int _passed;

    public void Expect(bool condition, string description)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS {description}");
            return;
        }

        _failures.Add(description);
        Console.Error.WriteLine($"FAIL {description}");
    }

    public int Finish()
    {
        int total = _passed + _failures.Count;
        Console.WriteLine($"{_passed}/{total} checks passed.");
        return _failures.Count == 0 ? 0 : 1;
    }
}
