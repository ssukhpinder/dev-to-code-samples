namespace JsonPatchSecurity;

public sealed record PatchDecision(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static PatchDecision Accepted() => new(true, []);

    public static PatchDecision Rejected(IEnumerable<string> errors) =>
        new(false, errors.ToArray());
}
