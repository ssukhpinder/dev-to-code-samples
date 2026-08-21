using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

const string packageId = "Demo.Greeting";
const string oldVersion = "1.0.0";
const string fixedVersion = "2.0.0";

var sampleRoot = FindSampleRoot();
var configPath = Path.Combine(sampleRoot, "NuGet.config");
var feedPath = Path.Combine(sampleRoot, "FixtureFeed");
var artifactsPath = Path.Combine(sampleRoot, "artifacts");
var fixtureProject = Path.Combine(sampleRoot, "FixturePackage", "FixturePackage.csproj");
var brokenProject = Path.Combine(sampleRoot, "BrokenDirect", "BrokenDirect.csproj");
var directProject = Path.Combine(sampleRoot, "FixedDirect", "FixedDirect.csproj");
var centralProject = Path.Combine(sampleRoot, "CentralManaged", "CentralManaged.csproj");
var centralProps = Path.Combine(sampleRoot, "CentralManaged", "Directory.Packages.props");
var passed = 0;

ResetGeneratedState();

EnsureSuccessful(
    "fixture restore",
    await RunDotNetAsync(
        "restore",
        fixtureProject,
        "--configfile",
        configPath,
        "--force",
        "--no-cache",
        "--nologo",
        "--verbosity",
        "minimal"));

foreach (var version in new[] { oldVersion, fixedVersion })
{
    EnsureSuccessful(
        $"pack fixture {version}",
        await RunDotNetAsync(
            "pack",
            fixtureProject,
            "--configuration",
            "Release",
            "--no-restore",
            "--output",
            feedPath,
            $"-p:Version={version}",
            $"-p:PackageVersion={version}",
            "--nologo",
            "--verbosity",
            "minimal"));
}

Check(
    "the local feed contains both fixture versions",
    File.Exists(Path.Combine(feedPath, $"{packageId}.{oldVersion}.nupkg"))
    && File.Exists(Path.Combine(feedPath, $"{packageId}.{fixedVersion}.nupkg")));

var brokenRestore = await RestoreAsync(brokenProject);
Check(
    "the default broken restore reports NU1015 for Demo.Greeting",
    brokenRestore.ExitCode != 0
    && brokenRestore.CombinedOutput.Contains("NU1015", StringComparison.Ordinal)
    && brokenRestore.CombinedOutput.Contains(packageId, StringComparison.Ordinal));

var compatibilityRestore = await RestoreAsync(
    brokenProject,
    "-p:SdkAnalysisLevel=9.0.300",
    "-p:TreatWarningsAsErrors=false");
Check(
    "the compatibility level restores the old NU1604 warning",
    compatibilityRestore.ExitCode == 0
    && compatibilityRestore.CombinedOutput.Contains("NU1604", StringComparison.Ordinal));
Check(
    "the compatibility level selects the lowest available version",
    ReadResolvedVersion(brokenProject, packageId) == oldVersion);

EnsureSuccessful(
    "compatibility build",
    await BuildAsync(brokenProject));
var compatibilityRun = await RunProjectAsync(brokenProject);
Check(
    "the compatibility build runs with package version 1.0.0",
    compatibilityRun.ExitCode == 0
    && compatibilityRun.StandardOutput.Trim() == "resolved=1.0.0");

var directDocument = XDocument.Load(directProject);
var directReference = FindPackageReference(directDocument, packageId);
Check(
    "the direct fix declares package version 2.0.0",
    directReference?.Attribute("Version")?.Value == fixedVersion);

EnsureSuccessful("direct restore", await RestoreAsync(directProject));
Check(
    "the direct fix resolves package version 2.0.0",
    ReadResolvedVersion(directProject, packageId) == fixedVersion);

EnsureSuccessful("direct build", await BuildAsync(directProject));
var directRun = await RunProjectAsync(directProject);
Check(
    "the direct fix runs with package version 2.0.0",
    directRun.ExitCode == 0
    && directRun.StandardOutput.Trim() == "resolved=2.0.0");

var centralDocument = XDocument.Load(centralProject);
var centralReference = FindPackageReference(centralDocument, packageId);
var propsDocument = XDocument.Load(centralProps);
Check(
    "Central Package Management is enabled",
    propsDocument.Descendants("ManagePackageVersionsCentrally").Single().Value == "true");
Check(
    "CPM omits the reference version and pins 2.0.0 centrally",
    centralReference?.Attribute("Version") is null
    && propsDocument.Descendants("PackageVersion").Single(
        element => element.Attribute("Include")?.Value == packageId)
        .Attribute("Version")?.Value == fixedVersion);

EnsureSuccessful("central restore", await RestoreAsync(centralProject));
Check(
    "Central Package Management resolves package version 2.0.0",
    ReadResolvedVersion(centralProject, packageId) == fixedVersion);

EnsureSuccessful("central build", await BuildAsync(centralProject));
var centralRun = await RunProjectAsync(centralProject);
Check(
    "Central Package Management runs with package version 2.0.0",
    centralRun.ExitCode == 0
    && centralRun.StandardOutput.Trim() == "resolved=2.0.0");

Console.WriteLine($"PASS {passed}/12");

void Check(string name, bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {name}");
    }

    passed++;
    Console.WriteLine($"PASS: {name}");
}

void ResetGeneratedState()
{
    var expectedArtifacts = Path.GetFullPath(Path.Combine(sampleRoot, "artifacts"));
    if (!string.Equals(expectedArtifacts, artifactsPath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Refusing to clean an unexpected artifacts path.");
    }

    if (Directory.Exists(artifactsPath))
    {
        Directory.Delete(artifactsPath, recursive: true);
    }

    foreach (var package in Directory.GetFiles(feedPath, $"{packageId}.*.nupkg"))
    {
        File.Delete(package);
    }
}

async Task<CommandResult> RestoreAsync(string projectPath, params string[] extraArguments)
{
    var arguments = new List<string>
    {
        "restore",
        projectPath,
        "--configfile",
        configPath,
        "--force",
        "--no-cache",
        "--nologo",
        "--verbosity",
        "minimal",
    };
    arguments.AddRange(extraArguments);
    return await RunDotNetAsync(arguments.ToArray());
}

Task<CommandResult> BuildAsync(string projectPath) =>
    RunDotNetAsync(
        "build",
        projectPath,
        "--configuration",
        "Release",
        "--no-restore",
        "--nologo",
        "--verbosity",
        "minimal");

Task<CommandResult> RunProjectAsync(string projectPath) =>
    RunDotNetAsync(
        "run",
        "--project",
        projectPath,
        "--configuration",
        "Release",
        "--no-build",
        "--no-restore");

static XElement? FindPackageReference(XDocument document, string id) =>
    document.Descendants("PackageReference").SingleOrDefault(
        element => element.Attribute("Include")?.Value == id);

static string ReadResolvedVersion(string projectPath, string id)
{
    var assetsPath = Path.Combine(
        Path.GetDirectoryName(projectPath)!,
        "obj",
        "project.assets.json");

    using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
    var prefix = string.Concat(id, "/");
    var matches = assets.RootElement
        .GetProperty("libraries")
        .EnumerateObject()
        .Where(property => property.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(property => property.Name[prefix.Length..])
        .ToArray();

    return matches.Length == 1
        ? matches[0]
        : throw new InvalidOperationException(
            $"Expected one resolved {id} version, found {matches.Length}.");
}

static void EnsureSuccessful(string name, CommandResult result)
{
    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"{name} failed.{Environment.NewLine}{result.CombinedOutput}");
    }
}

async Task<CommandResult> RunDotNetAsync(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(artifactsPath, "packages");
    startInfo.Environment.Remove("NUGET_FALLBACK_PACKAGES");

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
        if (File.Exists(Path.Combine(directory.FullName, "Nu1015PackageVersions.slnx")))
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
