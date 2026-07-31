namespace ExceptionPipeline;

public record Product(int Id, string Name, decimal Price);

// Domain exceptions: thrown deep in the app, mapped to HTTP exactly once, at the boundary.
public sealed class ProductNotFoundException(int id)
    : Exception($"Product {id} does not exist.")
{
    public int ProductId { get; } = id;
}

public sealed class StaleInventoryException(int id, int expected, int actual)
    : Exception($"Reservation for product {id} expected {expected} units but only {actual} remain.")
{
    public int ProductId { get; } = id;
}
