namespace JsonPatchGuardrails;

public sealed record Profile(string DisplayName, string TimeZone, bool IsAdmin);

public sealed class EditableProfile
{
    public string DisplayName { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;
}

public sealed record PatchOutcome(
    bool Succeeded,
    Profile Profile,
    IReadOnlyList<string> Errors);
