using System.Diagnostics;
using System.Text.Json;

var sampleRoot = FindSampleRoot();
var brokenProject = Path.Combine(sampleRoot, "BrokenConsumer", "BrokenConsumer.csproj");
var fixedProject = Path.Combine(sampleRoot, "FixedConsumer", "FixedConsumer.csproj");
var passed = 0;

var brokenReferences = await GetProjectReferencesAsync(brokenProject);
Check(
    "the DefineConstants evaluation gate drops the ProjectReference",
    brokenReferences.Count == 0);

var fixedReferences = await GetProjectReferencesAsync(fixedProject);
Check(
    "IsTargetFrameworkCompatible selects one ProjectReference",
    fixedReferences.Count == 1);
Check(
    "the fixed gate selects MarkerLibrary",
    fixedReferences.Single().EndsWith("MarkerLibrary.csproj", StringComparison.OrdinalIgnoreCase));

var brokenBuild = await RunDotNetAsync(
    "build",
    brokenProject,
    "--configuration",
    "Release",
    "--no-restore",
    "--nologo",
    "--verbosity",
    "minimal");
Check(
    "the broken project still builds, making the missing item easy to miss",
    brokenBuild.ExitCode == 0);

var brokenGate = await RunDotNetAsync(
    "msbuild",
    brokenProject,
    "-target:VerifyFrameworkReference",
    "-property:Configuration=Release",
    "-nologo",
    "-verbosity:minimal");
Check(
    "the explicit gate fails with the evaluation diagnostic",
    brokenGate.ExitCode != 0
    && brokenGate.CombinedOutput.Contains("DCG001", StringComparison.Ordinal));

var fixedBuild = await RunDotNetAsync(
    "build",
    fixedProject,
    "--configuration",
    "Release",
    "--no-restore",
    "--nologo",
    "--verbosity",
    "minimal");
Check("the fixed project builds", fixedBuild.ExitCode == 0);

var fixedRun = await RunDotNetAsync(
    "run",
    "--project",
    fixedProject,
    "--configuration",
    "Release",
    "--no-build",
    "--no-restore");
Check(
    "C# compile symbols remain available after the MSBuild evaluation change",
    fixedRun.ExitCode == 0
    && fixedRun.StandardOutput.Trim()
        == "reference=TFM gate active; compile-symbol=present");

Console.WriteLine($"PASS {passed}/7");

void Check(string name, bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {name}");
    }

    passed++;
    Console.WriteLine($"PASS: {name}");
}

static async Task<List<string>> GetProjectReferencesAsync(string projectPath)
{
    var result = await RunDotNetAsync(
        "msbuild",
        projectPath,
        "-nologo",
        "-getItem:ProjectReference");

    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"MSBuild evaluation failed.{Environment.NewLine}{result.CombinedOutput}");
    }

    var jsonStart = result.StandardOutput.IndexOf('{');
    var jsonEnd = result.StandardOutput.LastIndexOf('}');
    if (jsonStart < 0 || jsonEnd < jsonStart)
    {
        throw new InvalidOperationException("MSBuild did not return an item-evaluation document.");
    }

    using var document = JsonDocument.Parse(
        result.StandardOutput[jsonStart..(jsonEnd + 1)]);
    var references = document.RootElement
        .GetProperty("Items")
        .GetProperty("ProjectReference");

    return references
        .EnumerateArray()
        .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
        .ToList();
}

static async Task<CommandResult> RunDotNetAsync(params string[] arguments)
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

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    return new CommandResult(
        process.ExitCode,
        await standardOutput,
        await standardError);
}

static string FindSampleRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DefineConstantsConditions.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "Run the verifier from the sample folder or one of its child folders.");
}

internal sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput =>
        string.Concat(StandardOutput, Environment.NewLine, StandardError);
}
