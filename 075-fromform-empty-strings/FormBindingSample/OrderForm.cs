namespace FormBindingSample;

public sealed class OrderForm
{
    public int? Quantity { get; set; }

    public DateOnly? ShipDate { get; set; }
}

public sealed record BindingResult(int? Quantity, DateOnly? ShipDate);
