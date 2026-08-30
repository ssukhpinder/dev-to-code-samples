using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OrderContract.Tests;

[TestClass]
public sealed class DeepEquivalenceTests
{
    [TestMethod]
    public void Separately_allocated_graphs_are_equivalent()
    {
        OrderContract expected = CreateOrder();
        OrderContract actual = CreateOrder();

        Assert.AreNotSame(expected, actual);
        Assert.AreNotSame(expected.ShippingAddress, actual.ShippingAddress);
        Assert.AreEquivalent(expected, actual, strict: true);
    }

    [TestMethod]
    public void Nested_property_drift_reports_its_path()
    {
        OrderContract expected = CreateOrder();
        OrderContract actual = CreateOrder(city: "Calgary");

        AssertFailedException failure = Assert.ThrowsExactly<AssertFailedException>(
            () => Assert.AreEquivalent(expected, actual, strict: true));

        Assert.IsTrue(
            failure.Message.Contains("ShippingAddress.City", StringComparison.Ordinal),
            failure.Message);
    }

    [TestMethod]
    public void Enumerable_order_is_part_of_the_contract()
    {
        OrderContract expected = CreateOrder();
        OrderContract actual = CreateOrder(reverseLines: true);

        AssertFailedException failure = Assert.ThrowsExactly<AssertFailedException>(
            () => Assert.AreEquivalent(expected, actual, strict: true));

        Assert.IsTrue(failure.Message.Contains("Lines[0]", StringComparison.Ordinal), failure.Message);
    }

    [TestMethod]
    public void Strict_mode_rejects_extra_dictionary_keys()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["currency"] = "CAD",
        };
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CURRENCY"] = "CAD",
            ["debug"] = "true",
        };

        Assert.AreEquivalent(expected, actual);

        AssertFailedException failure = Assert.ThrowsExactly<AssertFailedException>(
            () => Assert.AreEquivalent(expected, actual, strict: true));

        Assert.IsTrue(failure.Message.Contains("debug", StringComparison.Ordinal), failure.Message);
    }

    [TestMethod]
    public void Equivalent_cycles_terminate()
    {
        var expected = new NodeContract { Name = "root" };
        expected.Next = expected;
        var actual = new NodeContract { Name = "root" };
        actual.Next = actual;

        Assert.AreEquivalent(expected, actual, strict: true);
    }

    [TestMethod]
    public void Shared_reference_topology_mismatch_is_rejected()
    {
        var shared = new NodeContract { Name = "shared" };
        var expected = new NodePair { Left = shared, Right = shared };
        var actual = new NodePair
        {
            Left = new NodeContract { Name = "shared" },
            Right = new NodeContract { Name = "shared" },
        };

        Assert.ThrowsExactly<AssertFailedException>(
            () => Assert.AreEquivalent(expected, actual, strict: true));
    }

    private static OrderContract CreateOrder(
        string city = "Edmonton",
        bool reverseLines = false)
    {
        var lines = new List<LineContract>
        {
            new() { Sku = "KEYBOARD", Quantity = 1 },
            new() { Sku = "CABLE", Quantity = 2 },
        };

        if (reverseLines)
        {
            lines.Reverse();
        }

        return new OrderContract
        {
            Id = "order-103",
            ShippingAddress = new AddressContract
            {
                City = city,
                PostalCode = "T5J 0N3",
            },
            Lines = lines,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["currency"] = "CAD",
            },
        };
    }
}

public sealed class OrderContract
{
    public required string Id { get; init; }

    public required AddressContract ShippingAddress { get; init; }

    public required List<LineContract> Lines { get; init; }

    public required Dictionary<string, string> Metadata { get; init; }
}

public sealed class AddressContract
{
    public required string City { get; init; }

    public required string PostalCode { get; init; }
}

public sealed class LineContract
{
    public required string Sku { get; init; }

    public required int Quantity { get; init; }
}

public sealed class NodeContract
{
    public required string Name { get; init; }

    public NodeContract? Next { get; set; }
}

public sealed class NodePair
{
    public required NodeContract Left { get; init; }

    public required NodeContract Right { get; init; }
}
