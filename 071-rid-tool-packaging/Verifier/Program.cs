using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

const string PackageId = "Rid.Pack.Demo";
const string Version = "1.0.0";

string sampleRoot = FindSampleRoot();
string projectPath = Path.Combine(sampleRoot, "Tool", "RidPackingDemo.csproj");
string artifactsRoot = Path.GetFullPath(Path.Combine(sampleRoot, "artifacts", "verifier"));

EnsureGeneratedPathIsSafe(sampleRoot, artifactsRoot);
if (Directory.Exists(artifactsRoot))
{
    Directory.Delete(artifactsRoot, recursive: true);
}

Directory.CreateDirectory(artifactsRoot);

var verifier = new Verifier();

string defaultOutput = Path.Combine(artifactsRoot, "default");
Pack(projectPath, defaultOutput);
string[] defaultFiles = PackageFileNames(defaultOutput);
verifier.Equal(
    [
        $"{PackageId}.{Version}.nupkg",
        $"{PackageId}.linux-x64.{Version}.nupkg",
        $"{PackageId}.win-x64.{Version}.nupkg",
    ],
    defaultFiles,
    "RuntimeIdentifiers creates a pointer package and one package per RID");

string defaultPointer = Path.Combine(defaultOutput, $"{PackageId}.{Version}.nupkg");
ToolSettings defaultSettings = ReadToolSettings(defaultPointer);
verifier.Equal("2", defaultSettings.Version, "the pointer uses version 2 tool settings");
verifier.Equal("rid-pack-demo", defaultSettings.CommandName, "the pointer keeps the tool command name");
verifier.Equal(
    [$"linux-x64={PackageId}.linux-x64", $"win-x64={PackageId}.win-x64"],
    defaultSettings.RuntimePackageMappings,
    "DotnetToolSettings.xml maps each RID to the correct package");
verifier.True(
    ContainsEntry(Path.Combine(defaultOutput, $"{PackageId}.win-x64.{Version}.nupkg"), "tools/net10.0/win-x64/RidPackingDemo.dll"),
    "the win-x64 package stores the tool under a RID-specific path");
verifier.True(
    ContainsEntry(Path.Combine(defaultOutput, $"{PackageId}.linux-x64.{Version}.nupkg"), "tools/net10.0/linux-x64/RidPackingDemo.dll"),
    "the linux-x64 package stores the tool under a RID-specific path");

string subsetOutput = Path.Combine(artifactsRoot, "subset");
Pack(projectPath, subsetOutput, "-p:ToolPackageRuntimeIdentifiers=win-x64");
verifier.Equal(
    [$"{PackageId}.{Version}.nupkg", $"{PackageId}.win-x64.{Version}.nupkg"],
    PackageFileNames(subsetOutput),
    "ToolPackageRuntimeIdentifiers limits the package set");
verifier.Equal(
    [$"win-x64={PackageId}.win-x64"],
    ReadToolSettings(Path.Combine(subsetOutput, $"{PackageId}.{Version}.nupkg")).RuntimePackageMappings,
    "the subset pointer maps only the selected RID");

string portableOutput = Path.Combine(artifactsRoot, "portable");
Pack(
    projectPath,
    portableOutput,
    "-p:RuntimeIdentifiers=",
    "-p:CreateRidSpecificToolPackages=false",
    "-p:UseAppHost=false");
string portablePackage = Path.Combine(portableOutput, $"{PackageId}.{Version}.nupkg");
ToolSettings portableSettings = ReadToolSettings(portablePackage);
verifier.Equal(
    [$"{PackageId}.{Version}.nupkg"],
    PackageFileNames(portableOutput),
    "the documented opt-out restores one portable package");
verifier.Equal("1", portableSettings.Version, "the portable package uses classic version 1 tool settings");
verifier.Equal("rid-pack-demo", portableSettings.CommandName, "the portable package keeps the tool command name");
verifier.Equal("RidPackingDemo.dll", portableSettings.EntryPoint, "the portable package points to the managed entry point");
verifier.Equal("dotnet", portableSettings.Runner, "the portable package uses the dotnet runner");
verifier.Equal(
    [],
    portableSettings.RuntimePackageMappings,
    "the portable package has no RID-package mappings");
verifier.True(
    ContainsEntry(portablePackage, "tools/net10.0/any/RidPackingDemo.dll"),
    "the portable package stores the framework-dependent tool under any");

verifier.True(
    defaultFiles.Length > PackageFileNames(portableOutput).Length,
    "the verifier catches artifact-count growth rather than relying on pack logs");

verifier.Finish();

static void Pack(string projectPath, string outputPath, params string[] properties)
{
    Directory.CreateDirectory(outputPath);

    RunDotnet(
        ["clean", projectPath, "--configuration", "Release", "--verbosity", "quiet"],
        "dotnet clean");

    var arguments = new List<string>
    {
        "pack",
        projectPath,
        "--configuration",
        "Release",
        "--no-restore",
        "--output",
        outputPath,
    };

    arguments.AddRange(properties);
    RunDotnet(arguments, "dotnet pack");
}

static void RunDotnet(IEnumerable<string> arguments, string operation)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    startInfo.Environment["DOTNET_NOLOGO"] = "1";

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start {operation}.");
    Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
    Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    string standardOutput = standardOutputTask.GetAwaiter().GetResult();
    string standardError = standardErrorTask.GetAwaiter().GetResult();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"{operation} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{standardError}");
    }
}

static string[] PackageFileNames(string directory) =>
    Directory.GetFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.Ordinal)
        .ToArray();

static ToolSettings ReadToolSettings(string packagePath)
{
    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    ZipArchiveEntry settings = archive.Entries.Single(entry => entry.FullName.EndsWith("DotnetToolSettings.xml", StringComparison.Ordinal));
    using Stream stream = settings.Open();
    XDocument document = XDocument.Load(stream);
    XElement root = document.Root
        ?? throw new InvalidOperationException("DotnetToolSettings.xml has no root element.");
    XElement command = root
        .Descendants()
        .Single(element => element.Name.LocalName == "Command");

    string[] mappings = root
        .Descendants()
        .Where(element => element.Name.LocalName == "RuntimeIdentifierPackage")
        .Select(element =>
            $"{RequiredAttribute(element, "RuntimeIdentifier")}={RequiredAttribute(element, "Id")}")
        .Order(StringComparer.Ordinal)
        .ToArray();

    return new ToolSettings(
        RequiredAttribute(root, "Version"),
        RequiredAttribute(command, "Name"),
        (string?)command.Attribute("EntryPoint"),
        (string?)command.Attribute("Runner"),
        mappings);
}

static string RequiredAttribute(XElement element, string name) =>
    (string?)element.Attribute(name)
    ?? throw new InvalidOperationException($"{element.Name.LocalName} is missing {name}.");

static bool ContainsEntry(string packagePath, string expectedPath)
{
    using ZipArchive archive = ZipFile.OpenRead(packagePath);
    return archive.Entries.Any(entry => string.Equals(entry.FullName, expectedPath, StringComparison.Ordinal));
}

static string FindSampleRoot()
{
    DirectoryInfo? current = new(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "RidToolPackaging.slnx")))
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

    public void Equal(string[] expected, string[] actual, string message)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"FAIL: {message} (expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}])");
        }

        True(true, message);
    }

    public void Equal(string expected, string? actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FAIL: {message} (expected {expected}, actual {actual ?? "<null>"})");
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

sealed record ToolSettings(
    string Version,
    string CommandName,
    string? EntryPoint,
    string? Runner,
    string[] RuntimePackageMappings);
