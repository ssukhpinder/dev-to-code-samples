using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

Console.OutputEncoding = Encoding.UTF8;

if (args is ["--legacy-child"])
{
    AppContext.SetSwitch("Switch.System.Xml.IgnoreObsoleteMembers", true);
    var childXml = Serialize(new LegacySwitchInvoice());
    Console.WriteLine($"Legacy-switch elements: {ElementNames(childXml)}");
    return !HasElement(childXml, "LegacyCode") && HasElement(childXml, "Id") ? 0 : 1;
}

var checks = new List<(string Name, bool Passed)>();

var defaultXml = Serialize(new DefaultInvoice());
var roundTripped = Deserialize<DefaultInvoice>(defaultXml);

Check("default includes warning-only obsolete member", HasElement(defaultXml, "LegacyCode"));
Check("XmlIgnore remains explicit exclusion", !HasElement(defaultXml, "InternalNote"));
Check(
    "warning-only obsolete member round-trips",
    string.Equals(
        typeof(DefaultInvoice).GetProperty("LegacyCode")?.GetValue(roundTripped) as string,
        "legacy-7",
        StringComparison.Ordinal));

Check(
    "IsError obsolete member blocks serializer creation",
    ThrowsInvalidOperationContaining(
        static () => _ = new XmlSerializer(typeof(ErrorInvoice)),
        "IsError"));

var legacyResult = RunLegacyProcess();

Check(
    "legacy AppContext switch restores old exclusion",
    legacyResult.ExitCode == 0 && !legacyResult.Output.Contains("LegacyCode", StringComparison.Ordinal));
Check(
    "legacy output keeps non-obsolete member",
    legacyResult.Output.EndsWith("Id", StringComparison.Ordinal));

Console.WriteLine($"Default elements: {ElementNames(defaultXml)}");
Console.WriteLine(legacyResult.Output);

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var passed = checks.Count(static check => check.Passed);
Console.WriteLine($"Verified {passed}/{checks.Count}");
return passed == checks.Count ? 0 : 1;

void Check(string name, bool passed) => checks.Add((name, passed));

static string Serialize<T>(T value)
{
    var serializer = new XmlSerializer(typeof(T));
    var namespaces = new XmlSerializerNamespaces();
    namespaces.Add(string.Empty, string.Empty);

    var settings = new XmlWriterSettings
    {
        OmitXmlDeclaration = true,
        Indent = false,
        NewLineHandling = NewLineHandling.None
    };

    using var writer = new StringWriter();
    using (var xmlWriter = XmlWriter.Create(writer, settings))
    {
        serializer.Serialize(xmlWriter, value, namespaces);
    }

    return writer.ToString();
}

static T Deserialize<T>(string xml)
{
    var serializer = new XmlSerializer(typeof(T));
    using var reader = new StringReader(xml);
    return (T)serializer.Deserialize(reader)!;
}

static bool HasElement(string xml, string localName) =>
    XDocument.Parse(xml).Root?.Elements().Any(
        element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal)) == true;

static string ElementNames(string xml) =>
    string.Join(", ", XDocument.Parse(xml).Root!.Elements().Select(element => element.Name.LocalName));

static bool ThrowsInvalidOperationContaining(Action action, string expectedText)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidOperationException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(expectedText, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

static (int ExitCode, string Output) RunLegacyProcess()
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
    var startInfo = new ProcessStartInfo
    {
        FileName = executable,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.ArgumentList.Add(
            Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("Entry assembly path is unavailable."));
    }

    startInfo.ArgumentList.Add("--legacy-child");

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the legacy-switch verifier.");
    var standardOutput = process.StandardOutput.ReadToEnd();
    var standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(standardError))
    {
        throw new InvalidOperationException($"Legacy-switch verifier wrote to stderr: {standardError.Trim()}");
    }

    return (process.ExitCode, standardOutput.Trim());
}

public sealed class DefaultInvoice
{
    public string Id { get; set; } = "INV-42";

    [Obsolete("Kept only for XML compatibility.")]
    public string LegacyCode { get; set; } = "legacy-7";

    [XmlIgnore]
    public string InternalNote { get; set; } = "not-on-the-wire";
}

public sealed class ErrorInvoice
{
    public string Id { get; set; } = "INV-42";

    [Obsolete("This member must not be serialized.", true)]
    public string RemovedCode { get; set; } = "removed";
}

public sealed class LegacySwitchInvoice
{
    public string Id { get; set; } = "INV-42";

    [Obsolete("Kept only for XML compatibility.")]
    public string LegacyCode { get; set; } = "legacy-7";
}
