using System.Text.Json;

namespace RuntimePatchVerifier;

internal static class Program
{
    private const string RuntimePackPrefix = "runtimepack.Microsoft.NETCore.App.Runtime.";

    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return RunSelfTests();
        }

        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: RuntimePatchVerifier <app.deps.json> <rid> <minimum-runtime-version>");
            Console.Error.WriteLine("       RuntimePatchVerifier --self-test");
            return 2;
        }

        if (!TryParseStableVersion(args[2], out Version? minimumVersion))
        {
            Console.Error.WriteLine($"FAIL: '{args[2]}' is not a stable numeric runtime version.");
            return 2;
        }

        if (!File.Exists(args[0]))
        {
            Console.Error.WriteLine($"FAIL: deps file not found: {args[0]}");
            return 1;
        }

        InspectionResult result;
        try
        {
            result = Inspect(File.ReadAllText(args[0]), args[1], minimumVersion);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"FAIL: could not read deps file: {exception.Message}");
            return 1;
        }

        if (!result.Success)
        {
            Console.Error.WriteLine($"FAIL: {result.Error}");
            return 1;
        }

        Console.WriteLine(
            $"PASS: {args[1]} contains .NET runtime {result.ActualVersion} " +
            $"(minimum {minimumVersion}).");
        Console.WriteLine($"Runtime pack: {result.RuntimePackKey}");
        return 0;
    }

    private static InspectionResult Inspect(string json, string rid, Version minimumVersion)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("runtimeTarget", out JsonElement runtimeTarget) ||
                !runtimeTarget.TryGetProperty("name", out JsonElement targetNameElement))
            {
                return InspectionResult.Fail("runtimeTarget.name is missing.");
            }

            string? targetName = targetNameElement.GetString();
            if (targetName is null || !targetName.EndsWith($"/{rid}", StringComparison.Ordinal))
            {
                return InspectionResult.Fail(
                    $"runtime target '{targetName ?? "<null>"}' does not match RID '{rid}'.");
            }

            if (!root.TryGetProperty("libraries", out JsonElement libraries) ||
                libraries.ValueKind != JsonValueKind.Object)
            {
                return InspectionResult.Fail("libraries is missing or is not an object.");
            }

            string expectedPrefix = $"{RuntimePackPrefix}{rid}/";
            string[] matches = libraries.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.StartsWith(expectedPrefix, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                return InspectionResult.Fail(
                    $"expected one runtime pack for '{rid}', found {matches.Length}.");
            }

            string runtimePackKey = matches[0];
            string versionText = runtimePackKey[expectedPrefix.Length..];
            if (!TryParseStableVersion(versionText, out Version? actualVersion))
            {
                return InspectionResult.Fail(
                    $"runtime pack version '{versionText}' is not a stable numeric version.");
            }

            if (actualVersion.Major != minimumVersion.Major ||
                actualVersion.Minor != minimumVersion.Minor)
            {
                return InspectionResult.Fail(
                    $"runtime {actualVersion} is outside the required " +
                    $"{minimumVersion.Major}.{minimumVersion.Minor} family.");
            }

            if (actualVersion < minimumVersion)
            {
                return InspectionResult.Fail(
                    $"runtime {actualVersion} is below required {minimumVersion}.");
            }

            if (!root.TryGetProperty("targets", out JsonElement targets) ||
                !targets.TryGetProperty(targetName, out JsonElement selectedTarget) ||
                !selectedTarget.TryGetProperty(runtimePackKey, out _))
            {
                return InspectionResult.Fail(
                    $"selected target '{targetName}' does not reference '{runtimePackKey}'.");
            }

            return InspectionResult.Pass(actualVersion, runtimePackKey);
        }
        catch (JsonException exception)
        {
            return InspectionResult.Fail($"invalid JSON: {exception.Message}");
        }
    }

    private static bool TryParseStableVersion(string value, out Version version)
    {
        bool parsed = Version.TryParse(value, out Version? candidate);
        if (!parsed || candidate is null || candidate.Build < 0 || candidate.Revision >= 0)
        {
            version = new Version();
            return false;
        }

        version = candidate;
        return true;
    }

    private static int RunSelfTests()
    {
        Version minimum = new(10, 0, 11);
        int passed = 0;

        Check(
            Inspect(Fixture("win-x64", "10.0.11"), "win-x64", minimum).Success,
            "accepts the minimum patch",
            ref passed);
        Check(
            Inspect(Fixture("win-x64", "10.0.12"), "win-x64", minimum).Success,
            "accepts a newer patch in the same family",
            ref passed);
        Check(
            HasFailure(Fixture("win-x64", "10.0.10"), "win-x64", minimum, "below required"),
            "rejects a stale patch",
            ref passed);
        Check(
            HasFailure(Fixture("linux-x64", "10.0.11"), "win-x64", minimum, "does not match RID"),
            "rejects a different RID",
            ref passed);
        Check(
            HasFailure(FixtureWithoutRuntimePack("win-x64"), "win-x64", minimum, "found 0"),
            "rejects a framework-dependent shape",
            ref passed);
        Check(
            HasFailure(Fixture("win-x64", "10.0.11-preview.1"), "win-x64", minimum, "stable numeric"),
            "rejects a prerelease version",
            ref passed);
        Check(
            HasFailure("{not-json", "win-x64", minimum, "invalid JSON"),
            "rejects malformed JSON",
            ref passed);
        Check(
            HasFailure(
                Fixture("win-x64", "10.0.11", includeTargetReference: false),
                "win-x64",
                minimum,
                "does not reference"),
            "rejects an unreferenced runtime pack",
            ref passed);

        Console.WriteLine($"Self-test result: {passed}/8 passed.");
        return passed == 8 ? 0 : 1;
    }

    private static bool HasFailure(
        string json,
        string rid,
        Version minimum,
        string expectedMessage)
    {
        InspectionResult result = Inspect(json, rid, minimum);
        return !result.Success && result.Error.Contains(expectedMessage, StringComparison.Ordinal);
    }

    private static void Check(bool condition, string name, ref int passed)
    {
        if (condition)
        {
            passed++;
            Console.WriteLine($"PASS {passed}: {name}");
            return;
        }

        Console.Error.WriteLine($"FAIL: {name}");
    }

    private static string Fixture(string rid, string version, bool includeTargetReference = true)
    {
        string runtimePackKey = $"{RuntimePackPrefix}{rid}/{version}";
        string selectedTarget = includeTargetReference
            ? $"\"{runtimePackKey}\": {{}}"
            : "\"PatchProbe/1.0.0\": {}";

        return $$"""
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/{{rid}}" },
              "targets": {
                ".NETCoreApp,Version=v10.0/{{rid}}": {
                  {{selectedTarget}}
                }
              },
              "libraries": {
                "{{runtimePackKey}}": { "type": "runtimepack" }
              }
            }
            """;
    }

    private static string FixtureWithoutRuntimePack(string rid) => $$"""
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/{{rid}}" },
          "targets": {
            ".NETCoreApp,Version=v10.0/{{rid}}": {
              "PatchProbe/1.0.0": {}
            }
          },
          "libraries": {
            "PatchProbe/1.0.0": { "type": "project" }
          }
        }
        """;

    private sealed record InspectionResult(
        bool Success,
        Version? ActualVersion,
        string? RuntimePackKey,
        string Error)
    {
        public static InspectionResult Pass(Version version, string runtimePackKey) =>
            new(true, version, runtimePackKey, string.Empty);

        public static InspectionResult Fail(string error) =>
            new(false, null, null, error);
    }
}
