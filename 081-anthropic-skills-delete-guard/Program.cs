using SkillsDeleteGuardSample;

var checks = 0;

void Check(bool condition, string failure)
{
    if (!condition)
    {
        throw new InvalidOperationException(failure);
    }

    checks++;
}

const string betaSkill = "skill_beta";
var betaApi = new FakeSkillsApi();
betaApi.SeedSkill(betaSkill, "skver_003", "skver_002", "skver_001");

var betaDelete = await betaApi.DeleteSkillAsync(
    betaSkill,
    DeleteContract.BetaHeader);

Check(
    betaDelete.StatusCode is 400 && !betaDelete.Deleted,
    "The beta contract should refuse to delete a versioned skill.");
Check(
    betaApi.SkillExists(betaSkill) && betaApi.GetVersionIds(betaSkill).Count is 3,
    "The beta refusal should retain the skill and every version.");

const string rawGaSkill = "skill_raw_ga";
var rawGaApi = new FakeSkillsApi();
rawGaApi.SeedSkill(rawGaSkill, "skver_003", "skver_002", "skver_001");
var rawGaVersionCount = rawGaApi.GetVersionIds(rawGaSkill).Count;

var rawGaDelete = await rawGaApi.DeleteSkillAsync(
    rawGaSkill,
    DeleteContract.GeneralAvailability);

Check(
    rawGaDelete is { StatusCode: 200, Deleted: true } &&
    rawGaVersionCount is 3 &&
    !rawGaApi.SkillExists(rawGaSkill),
    "The GA contract should delete the skill and all versions.");

const string guardedSkill = "skill_guarded";
var guardedApi = new FakeSkillsApi();
guardedApi.SeedSkill(guardedSkill, "skver_003", "skver_002", "skver_001");
var guard = new SkillsDeleteGuard(guardedApi);

var preview = await guard.PreviewAsync(guardedSkill);
Check(
    preview.VersionIds.SequenceEqual(
        ["skver_001", "skver_002", "skver_003"],
        StringComparer.Ordinal) &&
    guardedApi.ListCalls is 2,
    "The preview should collect every paginated version exactly once.");

var unapproved = await guard.DeleteAsync(preview, allowCascade: false);
Check(
    unapproved.Status is GuardedDeleteStatus.ApprovalRequired &&
    guardedApi.DeleteCalls is 0 &&
    guardedApi.SkillExists(guardedSkill),
    "The guard should block a cascade without explicit approval.");

guardedApi.AddVersion(guardedSkill, "skver_004");
var staleApproval = await guard.DeleteAsync(preview, allowCascade: true);
Check(
    staleApproval.Status is GuardedDeleteStatus.InventoryChanged &&
    guardedApi.DeleteCalls is 0,
    "The guard should reject an approval based on stale inventory.");

var freshPreview = await guard.PreviewAsync(guardedSkill);
var approved = await guard.DeleteAsync(freshPreview, allowCascade: true);
Check(
    approved is
    {
        Status: GuardedDeleteStatus.Deleted,
        ApprovedVersionCount: 4,
    } &&
    guardedApi.DeleteCalls is 1 &&
    !guardedApi.SkillExists(guardedSkill),
    "A fresh explicit approval should delete the skill and four versions.");

Console.WriteLine(
    $"Beta delete: HTTP {betaDelete.StatusCode}, retained " +
    $"{betaApi.GetVersionIds(betaSkill).Count} versions");
Console.WriteLine(
    $"GA raw delete: HTTP {rawGaDelete.StatusCode}, removed " +
    $"{rawGaVersionCount} versions");
Console.WriteLine($"Guard preview: {string.Join(", ", preview.VersionIds)}");
Console.WriteLine($"Guard without cascade approval: {unapproved.Status}");
Console.WriteLine($"Changed inventory: {staleApproval.Status}");
Console.WriteLine(
    $"Guard with fresh approval: {approved.Status}, approved " +
    $"{approved.ApprovedVersionCount} observed versions");
Console.WriteLine($"Checks passed: {checks}/7");
