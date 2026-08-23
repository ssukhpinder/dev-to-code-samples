using System.Diagnostics;

var sampleRoot = Environment.CurrentDirectory;
var legacyProject = Path.Combine(sampleRoot, "LegacyProbe", "LegacyProbe.csproj");
var fixedProject = Path.Combine(sampleRoot, "AotSafeValidation", "AotSafeValidation.csproj");

if (!File.Exists(legacyProject) || !File.Exists(fixedProject))
{
    Console.Error.WriteLine("Run the verifier from the 068-validationcontext-aot folder.");
    return 1;
}

var checks = new CheckRunner();

var legacy = RunDotnet(
    "build",
    legacyProject,
    "-c",
    "Release",
    "--no-restore",
    "--no-incremental");

checks.Check(legacy.ExitCode != 0, "legacy build fails under warnings-as-errors");
checks.Check(legacy.Output.Contains("IL2026", StringComparison.Ordinal), "legacy diagnostic is IL2026");
checks.Check(
    legacy.Output.Contains("ValidationContext.ValidationContext(Object)", StringComparison.Ordinal),
    "legacy diagnostic names the one-argument constructor");

var fixedBuild = RunDotnet(
    "build",
    fixedProject,
    "-c",
    "Release",
    "--no-restore",
    "--no-incremental");

checks.Check(fixedBuild.ExitCode == 0, "AOT-aware build succeeds");
checks.Check(!fixedBuild.Output.Contains("IL2026", StringComparison.Ordinal), "AOT-aware build contains no IL2026");

var fixedRun = RunDotnet(
    "run",
    "--project",
    fixedProject,
    "-c",
    "Release",
    "--no-build");

checks.Check(
    fixedRun.ExitCode == 0 && fixedRun.Output.Contains("PASS: 7/7 checks", StringComparison.Ordinal),
    "runtime contract passes 7/7 checks");

return checks.Complete();

static ProcessResult RunDotnet(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the dotnet process.");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    process.WaitForExit();

    return new ProcessResult(
        process.ExitCode,
        standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult());
}

internal sealed record ProcessResult(int ExitCode, string Output);

internal sealed class CheckRunner
{
    private int _passed;
    private int _total;

    public void Check(bool condition, string description)
    {
        _total++;
        if (!condition)
        {
            Console.Error.WriteLine($"FAIL: {description}");
            return;
        }

        _passed++;
        Console.WriteLine($"PASS: {description}");
    }

    public int Complete()
    {
        Console.WriteLine($"PASS: {_passed}/{_total} checks");
        return _passed == _total ? 0 : 1;
    }
}
