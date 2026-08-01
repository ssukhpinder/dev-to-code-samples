namespace JsonPatchAllowlist;

public sealed record Product(
    int Id,
    string DisplayName,
    decimal Price,
    bool IsFeatured);

public sealed class ProductPatch
{
    public string DisplayName { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

public sealed record PatchOutcome(
    bool Succeeded,
    Product Product,
    IReadOnlyList<string> Errors);
