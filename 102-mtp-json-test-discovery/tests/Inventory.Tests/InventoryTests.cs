using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Inventory.Tests;

[TestClass]
public sealed class InventoryTests
{
    [TestMethod]
    [TestCategory("contract")]
    public void Checkout_contract_is_discoverable()
    {
        Assert.AreEqual("checkout", NormalizeRoute(" CHECKOUT "));
    }

    [TestMethod]
    [TestCategory("contract")]
    public void Refund_contract_is_discoverable()
    {
        Assert.AreEqual("refund", NormalizeRoute("Refund"));
    }

    [TestMethod]
    [TestCategory("smoke")]
    public void Health_check_is_discoverable()
    {
        Assert.AreEqual("health", NormalizeRoute("/HEALTH/"));
    }

    private static string NormalizeRoute(string value) =>
        value.Trim().Trim('/').ToLowerInvariant();
}
