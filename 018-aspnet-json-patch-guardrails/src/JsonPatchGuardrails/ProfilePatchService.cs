using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;

namespace JsonPatchGuardrails;

public static class ProfilePatchService
{
    private const int MaxOperations = 8;

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/displayName",
        "/timeZone",
    };

    private static readonly HashSet<OperationType> AllowedOperations =
    [
        OperationType.Replace,
        OperationType.Test,
    ];

    private static readonly HashSet<string> AllowedTimeZones = new(StringComparer.Ordinal)
    {
        "America/Edmonton",
        "America/Toronto",
        "UTC",
    };

    public static PatchOutcome TryApply(
        Profile current,
        JsonPatchDocument<EditableProfile> patch)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);

        var errors = ValidateDocument(patch);
        if (errors.Count > 0)
        {
            return Rejected(current, errors);
        }

        // Apply to a disposable DTO, never directly to the tracked/domain object.
        var candidate = new EditableProfile
        {
            DisplayName = current.DisplayName,
            TimeZone = current.TimeZone,
        };

        patch.ApplyTo(candidate, error => errors.Add(error.ErrorMessage));
        ValidateCandidate(candidate, errors);

        if (errors.Count > 0)
        {
            return Rejected(current, errors);
        }

        return new PatchOutcome(
            true,
            current with
            {
                DisplayName = candidate.DisplayName.Trim(),
                TimeZone = candidate.TimeZone,
            },
            Array.Empty<string>());
    }

    private static List<string> ValidateDocument(JsonPatchDocument<EditableProfile> patch)
    {
        var errors = new List<string>();

        if (patch.Operations.Count > MaxOperations)
        {
            errors.Add($"A patch may contain at most {MaxOperations} operations.");
        }

        foreach (var operation in patch.Operations)
        {
            if (operation is null)
            {
                errors.Add("Patch operations cannot be null.");
                continue;
            }

            if (!AllowedOperations.Contains(operation.OperationType))
            {
                errors.Add($"Operation '{operation.op}' is not allowed.");
            }

            if (string.IsNullOrWhiteSpace(operation.path)
                || !AllowedPaths.Contains(operation.path))
            {
                errors.Add($"Path '{operation.path}' is not patchable.");
            }

            if (!string.IsNullOrWhiteSpace(operation.from))
            {
                errors.Add("The 'from' member is not allowed.");
            }
        }

        return errors;
    }

    private static void ValidateCandidate(EditableProfile candidate, List<string> errors)
    {
        var displayName = candidate.DisplayName?.Trim();

        if (string.IsNullOrEmpty(displayName) || displayName.Length > 80)
        {
            errors.Add("DisplayName must contain between 1 and 80 characters.");
        }

        if (!AllowedTimeZones.Contains(candidate.TimeZone))
        {
            errors.Add("TimeZone is not supported.");
        }
    }

    private static PatchOutcome Rejected(Profile current, List<string> errors) =>
        new(false, current, errors.ToArray());
}
