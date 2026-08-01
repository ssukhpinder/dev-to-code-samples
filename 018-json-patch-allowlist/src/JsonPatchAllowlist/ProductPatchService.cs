using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;

namespace JsonPatchAllowlist;

public static class ProductPatchService
{
    private const int MaxOperations = 8;

    private static readonly HashSet<string> AllowedPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/displayName",
            "/price",
        };

    private static readonly HashSet<OperationType> AllowedOperations =
    [
        OperationType.Replace,
        OperationType.Test,
    ];

    public static PatchOutcome TryApply(
        Product original,
        JsonPatchDocument<ProductPatch> patch)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(patch);

        var guardErrors = ValidateDocument(patch);
        if (guardErrors.Count > 0)
        {
            return Failed(original, guardErrors);
        }

        var candidate = new ProductPatch
        {
            DisplayName = original.DisplayName,
            Price = original.Price,
        };

        var applyFailed = false;
        patch.ApplyTo(candidate, _ => applyFailed = true);

        if (applyFailed)
        {
            // Do not reflect JsonPatchError.ErrorMessage to the caller. A failed
            // test operation can include both stored and attacker-supplied values.
            return Failed(original, ["The patch could not be applied."]);
        }

        var invariantErrors = ValidateCandidate(candidate);
        if (invariantErrors.Count > 0)
        {
            return Failed(original, invariantErrors);
        }

        var updated = original with
        {
            DisplayName = candidate.DisplayName,
            Price = candidate.Price,
        };

        return new PatchOutcome(true, updated, []);
    }

    private static List<string> ValidateDocument(
        JsonPatchDocument<ProductPatch> patch)
    {
        var errors = new List<string>();

        if (patch.Operations.Count is 0 or > MaxOperations)
        {
            errors.Add($"A patch must contain between 1 and {MaxOperations} operations.");
            return errors;
        }

        for (var index = 0; index < patch.Operations.Count; index++)
        {
            var operation = patch.Operations[index];

            if (!AllowedOperations.Contains(operation.OperationType))
            {
                errors.Add($"Operation {index} is not allowed.");
            }

            if (string.IsNullOrWhiteSpace(operation.path)
                || !AllowedPaths.Contains(operation.path))
            {
                errors.Add($"The path for operation {index} is not editable.");
            }
        }

        return errors;
    }

    private static List<string> ValidateCandidate(ProductPatch candidate)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(candidate.DisplayName)
            || candidate.DisplayName.Length > 80)
        {
            errors.Add("DisplayName must contain 1 to 80 characters.");
        }

        if (candidate.Price is < 0.01m or > 10_000m)
        {
            errors.Add("Price must be between 0.01 and 10000.");
        }

        return errors;
    }

    private static PatchOutcome Failed(
        Product original,
        IReadOnlyList<string> errors) =>
        new(false, original, errors);
}
