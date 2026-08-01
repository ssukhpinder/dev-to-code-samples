namespace JsonPatchSecurity;

public sealed class Customer
{
    public Customer(
        string id,
        string displayName,
        string email,
        bool isAdmin,
        decimal creditLimit)
    {
        Id = id;
        DisplayName = displayName;
        Email = email;
        IsAdmin = isAdmin;
        CreditLimit = creditLimit;
    }

    public string Id { get; }

    public string DisplayName { get; private set; }

    public string Email { get; private set; }

    public bool IsAdmin { get; }

    public decimal CreditLimit { get; }

    public void UpdateContact(string displayName, string email)
    {
        DisplayName = displayName;
        Email = email;
    }
}
