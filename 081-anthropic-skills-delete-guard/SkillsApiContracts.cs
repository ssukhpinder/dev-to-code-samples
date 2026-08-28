namespace SkillsDeleteGuardSample;

public enum DeleteContract
{
    BetaHeader,
    GeneralAvailability,
}

public sealed record SkillVersion(string Id);

public sealed record SkillVersionPage(
    IReadOnlyList<SkillVersion> Data,
    string? NextPage);

public sealed record SkillDeleteResponse(
    int StatusCode,
    bool Deleted,
    string Code);

public interface ISkillsApi
{
    Task<SkillVersionPage> ListVersionsAsync(
        string skillId,
        string? page,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SkillDeleteResponse> DeleteSkillAsync(
        string skillId,
        DeleteContract contract,
        CancellationToken cancellationToken = default);
}
