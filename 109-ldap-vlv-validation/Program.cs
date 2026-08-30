using System.DirectoryServices.Protocols;
using System.Reflection;
using System.Text;

#if NET10_0_OR_GREATER
const string expectedPackageVersion = "10.0.11";
const string highSurrogateExpectation = "unpaired high surrogate throws EncoderFallbackException";
const string lowSurrogateExpectation = "unpaired low surrogate throws EncoderFallbackException";
#else
const string expectedPackageVersion = "9.0.19";
const string highSurrogateExpectation = "unpaired high surrogate becomes EF-BF-BD";
const string lowSurrogateExpectation = "unpaired low surrogate becomes EF-BF-BD";
#endif

var checks = new (string Name, Action Verify)[]
{
    ("package version matches the target", () => VerifyPackageVersion(expectedPackageVersion)),
    ("ASCII target encodes as UTF-8", VerifyAsciiTarget),
    ("supplementary scalar target encodes as UTF-8", VerifySupplementaryScalarTarget),
    ("repeated GetValue calls return identical BER", VerifyDeterministicBer),
    (highSurrogateExpectation, () => VerifyMalformedTarget("\uD800")),
    (lowSurrogateExpectation, () => VerifyMalformedTarget("\uDC00")),
};

Console.WriteLine($"Target: {AppContext.TargetFrameworkName}");
Console.WriteLine($"Package: System.DirectoryServices.Protocols {GetPackageVersion()}");

var passed = 0;
foreach (var (name, verify) in checks)
{
    try
    {
        verify();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"Summary: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static void VerifyPackageVersion(string expected)
{
    Require(
        GetPackageVersion() == expected,
        $"Expected package {expected}, found {GetPackageVersion()}.");
}

static void VerifyAsciiTarget()
{
    VerifyValidTarget("alpha", Convert.FromHexString("616C706861"));
}

static void VerifySupplementaryScalarTarget()
{
    VerifyValidTarget(
        "caf\u00E9-\U0001F600",
        Convert.FromHexString("636166C3A92DF09F9880"));
}

static void VerifyValidTarget(string target, byte[] expectedUtf8)
{
    var control = new VlvRequestControl(2, 3, target);

    Require(control.BeforeCount == 2, "BeforeCount changed.");
    Require(control.AfterCount == 3, "AfterCount changed.");
    Require(
        control.Target.SequenceEqual(expectedUtf8),
        $"Expected {Convert.ToHexString(expectedUtf8)}, found {Convert.ToHexString(control.Target)}.");
}

static void VerifyDeterministicBer()
{
    var control = new VlvRequestControl(2, 3, "alpha");
    var first = control.GetValue();
    var second = control.GetValue();

    Require(first.Length > 0, "The BER control value was empty.");
    Require(first.SequenceEqual(second), "The BER control value changed between calls.");
}

static void VerifyMalformedTarget(string target)
{
#if NET10_0_OR_GREATER
    try
    {
        _ = new VlvRequestControl(2, 3, target);
    }
    catch (EncoderFallbackException)
    {
        return;
    }

    throw new InvalidOperationException(
        "The constructor accepted malformed UTF-16 instead of throwing EncoderFallbackException.");
#else
    var replacement = Convert.FromHexString("EFBFBD");
    var control = new VlvRequestControl(2, 3, target);
    var berValue = control.GetValue();

    Require(
        control.Target.SequenceEqual(replacement),
        $"Expected EF-BF-BD, found {Convert.ToHexString(control.Target)}.");
    Require(
        ContainsSequence(berValue, replacement),
        "The BER control value did not contain EF-BF-BD.");
#endif
}

#if !NET10_0_OR_GREATER
static bool ContainsSequence(byte[] value, byte[] expected) =>
    value.AsSpan().IndexOf(expected) >= 0;
#endif

static string GetPackageVersion()
{
    var informationalVersion = typeof(VlvRequestControl).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;

    return informationalVersion?.Split('+', 2)[0] ?? "unknown";
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
