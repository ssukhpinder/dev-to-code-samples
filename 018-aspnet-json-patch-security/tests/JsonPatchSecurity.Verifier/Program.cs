using System.Text.Json;
using JsonPatchSecurity;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

var checks = new (string Name, Action Run)[]
{
    ("safe contact fields are updated", SafePatchIsApplied),
    ("security-sensitive paths are rejected", SensitivePathIsRejected),
    ("copy operations are rejected", CopyOperationIsRejected),
    ("oversized documents are rejected", OversizedPatchIsRejected),
    ("ApplyTo failures do not partially mutate the domain object", ApplyFailureIsIsolated),
    ("business-rule failures do not mutate the domain object", InvalidEmailIsRejected),
};

var failures = new List<string>();

foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {check.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

Console.WriteLine($"{checks.Length - failures.Count}/{checks.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static void SafePatchIsApplied()
{
    var customer = NewCustomer();
    var decision = Apply(customer, """
        [
          { "op": "replace", "path": "/displayName", "value": "Grace Hopper" },
          { "op": "replace", "path": "/email", "value": "grace@example.com" }
        ]
        """);

    Assert(decision.Succeeded, string.Join("; ", decision.Errors));
    Assert(customer.DisplayName == "Grace Hopper", "DisplayName was not updated.");
    Assert(customer.Email == "grace@example.com", "Email was not updated.");
    Assert(customer.IsAdmin, "IsAdmin changed unexpectedly.");
    Assert(customer.CreditLimit == 10_000m, "CreditLimit changed unexpectedly.");
}

static void SensitivePathIsRejected()
{
    var customer = NewCustomer();
    var decision = Apply(customer, """
        [{ "op": "replace", "path": "/isAdmin", "value": false }]
        """);

    Assert(!decision.Succeeded, "The sensitive path should have been rejected.");
    Assert(customer.IsAdmin, "IsAdmin changed despite rejection.");
}

static void CopyOperationIsRejected()
{
    var customer = NewCustomer();
    var decision = Apply(customer, """
        [{ "op": "copy", "from": "/displayName", "path": "/email" }]
        """);

    Assert(!decision.Succeeded, "The copy operation should have been rejected.");
    Assert(customer.Email == "ada@example.com", "Email changed despite rejection.");
}

static void OversizedPatchIsRejected()
{
    var customer = NewCustomer();
    var operations = Enumerable.Range(1, CustomerPatchGuard.MaximumOperations + 1)
        .Select(index => new
        {
            op = "replace",
            path = "/displayName",
            value = $"Ada {index}",
        });

    var decision = Apply(customer, JsonSerializer.Serialize(operations));

    Assert(!decision.Succeeded, "The oversized patch should have been rejected.");
    Assert(customer.DisplayName == "Ada Lovelace", "DisplayName changed despite rejection.");
}

static void ApplyFailureIsIsolated()
{
    var customer = NewCustomer();
    var decision = Apply(customer, """
        [
          { "op": "replace", "path": "/displayName", "value": "Changed on the copy" },
          { "op": "replace", "path": "/email", "value": { "nested": true } }
        ]
        """);

    Assert(!decision.Succeeded, "The incompatible email value should have failed.");
    Assert(customer.DisplayName == "Ada Lovelace", "A partial mutation reached the domain object.");
    Assert(customer.Email == "ada@example.com", "Email changed despite ApplyTo failure.");
}

static void InvalidEmailIsRejected()
{
    var customer = NewCustomer();
    var decision = Apply(customer, """
        [{ "op": "replace", "path": "/email", "value": "not-an-email" }]
        """);

    Assert(!decision.Succeeded, "The invalid email should have been rejected.");
    Assert(customer.Email == "ada@example.com", "Email changed despite invariant failure.");
}

static PatchDecision Apply(Customer customer, string json)
{
    var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var patch = JsonSerializer.Deserialize<JsonPatchDocument<CustomerPatchInput>>(json, webJson)
        ?? throw new InvalidOperationException("The JSON Patch document could not be deserialized.");

    return new CustomerPatchGuard().ValidateAndApply(customer, patch);
}

static Customer NewCustomer() =>
    new("customer-42", "Ada Lovelace", "ada@example.com", isAdmin: true, creditLimit: 10_000m);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
