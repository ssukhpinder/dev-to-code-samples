using System.ComponentModel.DataAnnotations;

var options = new CheckoutOptions();
var policy = new CheckoutPolicy("north-store");
var services = new SingleServiceProvider(policy);
var sourceItems = new Dictionary<object, object?>
{
    ["tenant"] = "north-store",
};

var context = new ValidationContext(
    options,
    displayName: "Checkout customer",
    serviceProvider: services,
    items: sourceItems)
{
    MemberName = nameof(CheckoutOptions.CustomerId),
};

sourceItems["tenant"] = "changed-after-construction";

var required = new RequiredAttribute();
var missingResult = required.GetValidationResult(options.CustomerId, context);

var checks = new CheckRunner();
checks.Check(context.DisplayName == "Checkout customer", "explicit display name is preserved");
checks.Check(ReferenceEquals(context.ObjectInstance, options), "object instance is preserved");
checks.Check((string?)context.Items["tenant"] == "north-store", "items are copied at construction");
checks.Check(ReferenceEquals(context.GetService(typeof(CheckoutPolicy)), policy), "service provider remains available");
checks.Check(missingResult is not null, "known-property validation rejects a missing value");
checks.Check(
    missingResult?.ErrorMessage == "The Checkout customer field is required.",
    "validation message uses the explicit display name");

options.CustomerId = "customer-42";
var presentResult = required.GetValidationResult(options.CustomerId, context);
checks.Check(presentResult is null, "known-property validation accepts a present value");

return checks.Complete();

internal sealed class CheckoutOptions
{
    public string? CustomerId { get; set; }
}

internal sealed record CheckoutPolicy(string Tenant);

internal sealed class SingleServiceProvider(object service) : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        serviceType.IsInstanceOfType(service) ? service : null;
}

internal sealed class CheckRunner
{
    private int _passed;
    private int _total;

    public void Check(bool condition, string description)
    {
        _total++;
        if (!condition)
        {
            Console.Error.WriteLine($"FAIL: {description}");
            return;
        }

        _passed++;
        Console.WriteLine($"PASS: {description}");
    }

    public int Complete()
    {
        Console.WriteLine($"PASS: {_passed}/{_total} checks");
        return _passed == _total ? 0 : 1;
    }
}
