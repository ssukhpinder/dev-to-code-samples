using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

const string PackageId = "Nu1510.MultiTargetFixture";
const string PackageVersion = "1.0.0";

string sampleRoot = FindSampleRoot();
string brokenProject = Path.Combine(sampleRoot, "BrokenNet10", "BrokenNet10.csproj");
string fixedProject = Path.Combine(sampleRoot, "FixedNet10", "FixedNet10.csproj");
string multiTargetProject = Path.Combine(sampleRoot, "MultiTargetLibrary", "MultiTargetLibrary.csproj");
string artifactsRoot = Path.GetFullPath(Path.Combine(sampleRoot, "artifacts", "verifier"));

EnsureGeneratedPathIsSafe(sampleRoot, artifactsRoot);
if (Directory.Exists(artifactsRoot))
{
    Directory.Delete(artifactsRoot, recursive: true);
}

Directory.CreateDirectory(artifactsRoot);
var verifier = new Verifier();

CommandResult brokenRestore = RunDotnet(
    sampleRoot,
    "restore",
    brokenProject,
    "--force",
    "--no-cache");

verifier.True(brokenRestore.ExitCode != 0, "the net10-only redundant reference fails restore under warnings-as-errors");
verifier.Contains("NU1510", brokenRestore.CombinedOutput, "the failure reports NU1510");
verifier.Contains("System.Text.Json", brokenRestore.CombinedOutput, "the diagnostic names the redundant package");

RequireSuccess(
    RunDotnet(sampleRoot, "restore", fixedProject, "--force", "--no-cache"),
    "fixed net10 restore");
RequireSuccess(
    RunDotnet(sampleRoot, "build", fixedProject, "--configuration", "Release", "--no-restore"),
    "fixed net10 build");

CommandResult fixedRun = RunDotnet(
    sampleRoot,
    "run",
    "--project",
    fixedProject,
    "--configuration",
    "Release",
    "--no-build",
    "--no-restore");
RequireSuccess(fixedRun, "fixed net10 run");
verifier.Equal(
    "{\"Message\":\"framework-provided\",\"Count\":10}",
    fixedRun.StandardOutput.Trim(),
    "System.Text.Json still works after removing the package reference");

string fixedAssets = Path.Combine(sampleRoot, "FixedNet10", "obj", "project.assets.json");
verifier.False(
    AssetsContainPackage(fixedAssets, "System.Text.Json"),
    "the fixed net10 dependency graph has no System.Text.Json package");

RequireSuccess(
    RunDotnet(sampleRoot, "restore", multiTargetProject, "--force", "--no-cache"),
    "multi-target restore");
RequireSuccess(
    RunDotnet(sampleRoot, "build", multiTargetProject, "--configuration", "Release", "--no-restore"),
    "multi-target build");
RequireSuccess(
    RunDotnet(
        sampleRoot,
        "pack",
        multiTargetProject,
        "--configuration",
        "Release",
        "--no-build",
        "--no-restore",
        "--output",
        artifactsRoot),
    "multi-target pack");

string packagePath = Path.Combine(artifactsRoot, $"{PackageId}.{PackageVersion}.nupkg");
DependencyGroups groups = ReadDependencyGroups(packagePath);
verifier.True(
    groups.NetStandard20.Contains("System.Text.Json", StringComparer.OrdinalIgnoreCase),
    "the package keeps System.Text.Json for netstandard2.0");
verifier.False(
    groups.Net10.Contains("System.Text.Json", StringComparer.OrdinalIgnoreCase),
    "the package omits System.Text.Json from its net10.0 dependency group");

string multiAssets = Path.Combine(sampleRoot, "MultiTargetLibrary", "obj", "project.assets.json");
verifier.True(
    FrameworkDependenciesContainPackage(multiAssets, "netstandard2.0", "System.Text.Json"),
    "the conditioned reference is present for netstandard2.0");
verifier.False(
    FrameworkDependenciesContainPackage(multiAssets, "net10.0", "System.Text.Json"),
    "the conditioned reference is absent for net10.0");

verifier.Finish();

static CommandResult RunDotnet(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = workingDirectory,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    startInfo.Environment["DOTNET_NOLOGO"] = "1";
    startInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start dotnet.");
    Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
    Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();

    return new CommandResult(
        process.ExitCode,
        standardOutputTask.GetAwaiter().GetResult(),
        standardErrorTask.GetAwaiter().GetResult());
}

static void RequireSuccess(CommandResult result, string operation)
{
    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"{operation} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.CombinedOutput}");
    }
}

static bool AssetsContainPackage(string assetsPath, string packageId)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
    return document.RootElement
        .GetProperty("libraries")
        .EnumerateObject()
        .Any(library => library.Name.StartsWith($"{packageId}/", StringComparison.OrdinalIgnoreCase));
}

static bool FrameworkDependenciesContainPackage(string assetsPath, string framework, string packageId)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
    JsonElement frameworks = document.RootElement.GetProperty("project").GetProperty("frameworks");

    foreach (JsonProperty candidate in frameworks.EnumerateObject())
    {
        if (!string.Equals(candidate.Name, framework, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return candidate.Value.TryGetProperty("dependencies", out JsonElement dependencies)
            && dependencies.TryGetProperty(packageId, out _);
    }

    throw new InvalidOperationException($"Framework {framework} was not found in {assetsPath}.");
}

static DependencyGroups ReadDependencyGroups(string packagePath)
{
    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    ZipArchiveEntry nuspec = archive.Entries.Single(
        entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    using Stream stream = nuspec.Open();
    XDocument document = XDocument.Load(stream);

    string[] DependenciesFor(string targetFramework) => document
        .Descendants()
        .Where(element => element.Name.LocalName == "group")
        .Where(element => string.Equals(
            (string?)element.Attribute("targetFramework"),
            targetFramework,
            StringComparison.OrdinalIgnoreCase))
        .SelectMany(group => group.Elements().Where(element => element.Name.LocalName == "dependency"))
        .Select(dependency => (string?)dependency.Attribute("id"))
        .OfType<string>()
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return new DependencyGroups(
        DependenciesFor(".NETStandard2.0"),
        DependenciesFor("net10.0"));
}

static string FindSampleRoot()
{
    DirectoryInfo? current = new(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "Nu1510PackagePruning.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Run the verifier from the sample folder or one of its descendants.");
}

static void EnsureGeneratedPathIsSafe(string sampleRoot, string generatedPath)
{
    string expectedParent = Path.GetFullPath(Path.Combine(sampleRoot, "artifacts")) + Path.DirectorySeparatorChar;
    if (!generatedPath.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Refusing to clean a path outside this sample's artifacts folder.");
    }
}

sealed class Verifier
{
    private int passed;
    private int total;

    public void True(bool condition, string message)
    {
        total++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {message}");
        }

        passed++;
        Console.WriteLine($"PASS: {message}");
    }

    public void False(bool condition, string message) => True(!condition, message);

    public void Contains(string expected, string actual, string message)
    {
        True(actual.Contains(expected, StringComparison.OrdinalIgnoreCase), message);
    }

    public void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FAIL: {message} (expected {expected}, actual {actual})");
        }

        True(true, message);
    }

    public void Finish()
    {
        Console.WriteLine($"PASS {passed}/{total}");
        if (passed != total)
        {
            Environment.ExitCode = 1;
        }
    }
}

sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
}

sealed record DependencyGroups(string[] NetStandard20, string[] Net10);
