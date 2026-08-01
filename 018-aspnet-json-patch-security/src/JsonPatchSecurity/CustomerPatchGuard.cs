using System.Net.Mail;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;

namespace JsonPatchSecurity;

public sealed class CustomerPatchGuard
{
    public const int MaximumOperations = 5;

    private static readonly HashSet<string> AllowedPaths =
        ["/displayName", "/email"];

    public PatchDecision ValidateAndApply(
        Customer customer,
        JsonPatchDocument<CustomerPatchInput>? patch)
    {
        if (patch is null)
        {
            return PatchDecision.Rejected(["A JSON Patch document is required."]);
        }

        var policyErrors = ValidateDocument(patch);
        if (policyErrors.Count > 0)
        {
            return PatchDecision.Rejected(policyErrors);
        }

        // ApplyTo mutates its target. Work on a disposable input copy so a later
        // operation or invariant failure cannot partially change the domain object.
        var candidate = CustomerPatchInput.From(customer);
        var applyErrors = new List<string>();
        patch.ApplyTo(candidate, error => applyErrors.Add(error.ErrorMessage));

        if (applyErrors.Count > 0)
        {
            return PatchDecision.Rejected(applyErrors);
        }

        var invariantErrors = ValidateCandidate(candidate);
        if (invariantErrors.Count > 0)
        {
            return PatchDecision.Rejected(invariantErrors);
        }

        customer.UpdateContact(candidate.DisplayName, candidate.Email);
        return PatchDecision.Accepted();
    }

    private static List<string> ValidateDocument(
        JsonPatchDocument<CustomerPatchInput> patch)
    {
        var errors = new List<string>();

        if (patch.Operations.Count == 0)
        {
            errors.Add("At least one patch operation is required.");
        }

        if (patch.Operations.Count > MaximumOperations)
        {
            errors.Add($"A patch can contain at most {MaximumOperations} operations.");
        }

        foreach (var operation in patch.Operations)
        {
            if (operation.OperationType != OperationType.Replace)
            {
                errors.Add($"Operation '{operation.op}' is not allowed; use 'replace'.");
            }

            if (operation.path is null || !AllowedPaths.Contains(operation.path))
            {
                errors.Add($"Path '{operation.path}' is not patchable.");
            }
        }

        return errors;
    }

    private static List<string> ValidateCandidate(CustomerPatchInput candidate)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(candidate.DisplayName) || candidate.DisplayName.Length > 80)
        {
            errors.Add("DisplayName must contain 1 to 80 characters.");
        }

        if (!MailAddress.TryCreate(candidate.Email, out var parsed) ||
            !string.Equals(parsed.Address, candidate.Email, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Email must be a valid mailbox address.");
        }

        return errors;
    }
}
