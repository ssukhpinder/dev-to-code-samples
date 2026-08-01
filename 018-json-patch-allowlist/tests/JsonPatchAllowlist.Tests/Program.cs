using JsonPatchAllowlist;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using System.Text.Json;

var checks = new (string Name, Action Run)[]
{
    ("allowed replacement preserves protected fields", AllowedReplacement),
    ("protected path is rejected", ProtectedPath),
    ("copy operation is rejected", CopyOperation),
    ("operation limit is enforced", OperationLimit),
    ("failed test leaves the domain object unchanged", FailedTest),
    ("invalid business value is rejected", InvalidBusinessValue),
};

var passed = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS {check.Name}");
        passed++;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {check.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{passed}/{checks.Length} checks passed");
return passed == checks.Length ? 0 : 1;

static Product ExistingProduct() =>
    new(42, "Mechanical keyboard", 129.00m, IsFeatured: true);

static void AllowedReplacement()
{
    var original = ExistingProduct();
    var patch = FromWire(
        """
        [
          { "op": "replace", "path": "/displayName", "value": "Quiet keyboard" },
          { "op": "replace", "path": "/price", "value": 119.00 }
        ]
        """);

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(true, outcome.Succeeded);
    Equal("Quiet keyboard", outcome.Product.DisplayName);
    Equal(119.00m, outcome.Product.Price);
    Equal(original.Id, outcome.Product.Id);
    Equal(original.IsFeatured, outcome.Product.IsFeatured);
}

static void ProtectedPath()
{
    var original = ExistingProduct();
    var patch = FromWire(
        """
        [
          { "op": "replace", "path": "/isFeatured", "value": false }
        ]
        """);

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(false, outcome.Succeeded);
    Same(original, outcome.Product);
    Contains("not editable", outcome.Errors);
}

static void CopyOperation()
{
    var original = ExistingProduct();
    var patch = new JsonPatchDocument<ProductPatch>();
    patch.Copy(product => product.DisplayName, product => product.DisplayName);

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(false, outcome.Succeeded);
    Same(original, outcome.Product);
    Contains("not allowed", outcome.Errors);
}

static void OperationLimit()
{
    var original = ExistingProduct();
    var patch = new JsonPatchDocument<ProductPatch>();

    for (var index = 0; index < 9; index++)
    {
        patch.Replace(product => product.Price, 100m + index);
    }

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(false, outcome.Succeeded);
    Same(original, outcome.Product);
    Contains("between 1 and 8", outcome.Errors);
}

static void FailedTest()
{
    var original = ExistingProduct();
    var patch = new JsonPatchDocument<ProductPatch>();
    patch.Replace(product => product.Price, 99.00m);
    patch.Test(product => product.DisplayName, "Different name");

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(false, outcome.Succeeded);
    Same(original, outcome.Product);
    Equal(129.00m, original.Price);
}

static void InvalidBusinessValue()
{
    var original = ExistingProduct();
    var patch = new JsonPatchDocument<ProductPatch>();
    patch.Replace(product => product.Price, -1.00m);

    var outcome = ProductPatchService.TryApply(original, patch);

    Equal(false, outcome.Succeeded);
    Same(original, outcome.Product);
    Contains("between 0.01 and 10000", outcome.Errors);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Same(object expected, object actual)
{
    if (!ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException("The original domain object was replaced.");
    }
}

static void Contains(string expected, IEnumerable<string> actual)
{
    if (!actual.Any(message => message.Contains(expected, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException($"No error contained '{expected}'.");
    }
}

static JsonPatchDocument<ProductPatch> FromWire(string json) =>
    JsonSerializer.Deserialize<JsonPatchDocument<ProductPatch>>(
        json,
        new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("The JSON Patch payload was null.");
