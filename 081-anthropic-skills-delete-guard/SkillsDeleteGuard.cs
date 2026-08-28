namespace SkillsDeleteGuardSample;

public sealed record SkillDeletePlan(
    string SkillId,
    IReadOnlyList<string> VersionIds);

public enum GuardedDeleteStatus
{
    Deleted,
    ApprovalRequired,
    InventoryChanged,
    ApiRejected,
}

public sealed record GuardedDeleteResult(
    GuardedDeleteStatus Status,
    int ApprovedVersionCount,
    string Message);

public sealed class SkillsDeleteGuard(ISkillsApi api)
{
    private const int PageSize = 2;

    public async Task<SkillDeletePlan> PreviewAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

        var versionIds = new HashSet<string>(StringComparer.Ordinal);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? page = null;

        do
        {
            var response = await api.ListVersionsAsync(
                skillId,
                page,
                PageSize,
                cancellationToken);

            foreach (var version in response.Data)
            {
                if (!versionIds.Add(version.Id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate version returned: {version.Id}");
                }
            }

            if (response.NextPage is not null &&
                !seenCursors.Add(response.NextPage))
            {
                throw new InvalidOperationException("The version cursor repeated.");
            }

            page = response.NextPage;
        }
        while (page is not null);

        var orderedIds = versionIds.Order(StringComparer.Ordinal).ToArray();
        return new SkillDeletePlan(skillId, Array.AsReadOnly(orderedIds));
    }

    public async Task<GuardedDeleteResult> DeleteAsync(
        SkillDeletePlan approvedPlan,
        bool allowCascade,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedPlan);

        var currentPlan = await PreviewAsync(
            approvedPlan.SkillId,
            cancellationToken);

        if (!approvedPlan.VersionIds.SequenceEqual(
                currentPlan.VersionIds,
                StringComparer.Ordinal))
        {
            return new GuardedDeleteResult(
                GuardedDeleteStatus.InventoryChanged,
                0,
                "Version inventory changed; create and approve a new plan.");
        }

        if (currentPlan.VersionIds.Count > 0 && !allowCascade)
        {
            return new GuardedDeleteResult(
                GuardedDeleteStatus.ApprovalRequired,
                0,
                $"Deletion would remove {currentPlan.VersionIds.Count} version(s).");
        }

        var response = await api.DeleteSkillAsync(
            approvedPlan.SkillId,
            DeleteContract.GeneralAvailability,
            cancellationToken);

        return response.Deleted
            ? new GuardedDeleteResult(
                GuardedDeleteStatus.Deleted,
                currentPlan.VersionIds.Count,
                "Skill delete accepted for the approved version inventory.")
            : new GuardedDeleteResult(
                GuardedDeleteStatus.ApiRejected,
                0,
                $"Delete failed with HTTP {response.StatusCode}: {response.Code}");
    }
}
