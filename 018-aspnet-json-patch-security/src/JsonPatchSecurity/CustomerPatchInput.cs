namespace JsonPatchSecurity;

public sealed class CustomerPatchInput
{
    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public static CustomerPatchInput From(Customer customer) =>
        new()
        {
            DisplayName = customer.DisplayName,
            Email = customer.Email,
        };
}
