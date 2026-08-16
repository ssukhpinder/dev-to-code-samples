using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MtpCrashTrx;

[TestClass]
public sealed class CrashEvidenceTests
{
    private const string CrashVariable = "DEMO_CRASH";

    [TestMethod]
    public void A_CompletedBeforeCrash()
    {
        var runtimeValue = Environment.TickCount64;
        Assert.IsTrue(runtimeValue >= 0, "This passing result should be flushed to the TRX first.");
    }

    [TestMethod]
    public void B_CrashHostOnlyWhenRequested()
    {
        var crashRequested = Environment.GetEnvironmentVariable(CrashVariable);
        if (StringComparer.Ordinal.Equals(crashRequested, "1"))
        {
            Console.Error.WriteLine("DEMO_CRASH=1: intentionally terminating the test host.");
            Environment.FailFast("Intentional crash for the MTP dump and streamed-TRX demonstration.");
        }

        Assert.IsNull(crashRequested, "Leave DEMO_CRASH unset for a normal passing run.");
    }

    [TestMethod]
    public void C_WouldRunAfterCrash()
    {
        Assert.IsNull(
            Environment.GetEnvironmentVariable(CrashVariable),
            "The crash run should terminate before this test starts.");
    }
}
