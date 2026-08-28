namespace SkillsDeleteGuardSample;

public sealed class FakeSkillsApi : ISkillsApi
{
    private const string CursorPrefix = "opaque-page:";
    private readonly Dictionary<string, List<string>> _skills =
        new(StringComparer.Ordinal);

    public int DeleteCalls { get; private set; }

    public int ListCalls { get; private set; }

    public void SeedSkill(string skillId, params string[] versionIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentNullException.ThrowIfNull(versionIds);

        _skills[skillId] = versionIds
            .OrderByDescending(static id => id, StringComparer.Ordinal)
            .ToList();
    }

    public void AddVersion(string skillId, string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        if (!_skills.TryGetValue(skillId, out var versions))
        {
            throw new InvalidOperationException($"Unknown skill: {skillId}");
        }

        versions.Insert(0, versionId);
    }

    public bool SkillExists(string skillId) => _skills.ContainsKey(skillId);

    public IReadOnlyList<string> GetVersionIds(string skillId) =>
        _skills.TryGetValue(skillId, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<string>();

    public Task<SkillVersionPage> ListVersionsAsync(
        string skillId,
        string? page,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ListCalls++;

        if (!_skills.TryGetValue(skillId, out var versions))
        {
            return Task.FromResult(
                new SkillVersionPage(Array.Empty<SkillVersion>(), null));
        }

        var offset = DecodeCursor(page);
        if (offset > versions.Count)
        {
            throw new InvalidOperationException("The fake received an invalid page cursor.");
        }

        var data = versions
            .Skip(offset)
            .Take(limit)
            .Select(static id => new SkillVersion(id))
            .ToArray();

        var nextOffset = offset + data.Length;
        var nextPage = nextOffset < versions.Count
            ? $"{CursorPrefix}{nextOffset}"
            : null;

        return Task.FromResult(new SkillVersionPage(data, nextPage));
    }

    public Task<SkillDeleteResponse> DeleteSkillAsync(
        string skillId,
        DeleteContract contract,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        DeleteCalls++;

        if (!_skills.TryGetValue(skillId, out var versions))
        {
            return Task.FromResult(
                new SkillDeleteResponse(404, false, "skill_not_found"));
        }

        if (contract is DeleteContract.BetaHeader && versions.Count > 0)
        {
            return Task.FromResult(
                new SkillDeleteResponse(400, false, "versions_exist"));
        }

        _skills.Remove(skillId);

        return Task.FromResult(
            new SkillDeleteResponse(200, true, "skill_deleted"));
    }

    private static int DecodeCursor(string? page)
    {
        if (page is null)
        {
            return 0;
        }

        if (!page.StartsWith(CursorPrefix, StringComparison.Ordinal) ||
            !int.TryParse(page.AsSpan(CursorPrefix.Length), out var offset) ||
            offset < 0)
        {
            throw new InvalidOperationException("The fake received an invalid page cursor.");
        }

        return offset;
    }
}
