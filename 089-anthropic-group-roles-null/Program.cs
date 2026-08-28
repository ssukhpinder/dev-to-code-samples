using System.Text.Json;

var verification = new VerificationSuite();
verification.Run();

internal sealed class VerificationSuite
{
    private int _passed;

    public void Run()
    {
        const string mixedPageJson = """
            {
              "data": [
                {
                  "type": "rbac_group",
                  "id": "rbac_group_attached",
                  "name": "Incident Response",
                  "source_type": "direct",
                  "roles": ["rbac_role_incident", "rbac_role_audit"]
                },
                {
                  "type": "rbac_group",
                  "id": "rbac_group_empty",
                  "name": "New Starters",
                  "source_type": "scim",
                  "roles": []
                },
                {
                  "type": "rbac_group",
                  "id": "rbac_group_degraded",
                  "name": "Security Operations",
                  "source_type": "direct",
                  "roles": null
                }
              ],
              "has_more": true,
              "next_page": "page_next"
            }
            """;

        var mixedPage = RbacGroupPageParser.Parse(mixedPageJson);
        Verify(mixedPage.Groups.Count == 3, "all group fixtures were parsed");
        Verify(
            mixedPage.Groups.Select(group => group.Id).SequenceEqual(
                ["rbac_group_attached", "rbac_group_empty", "rbac_group_degraded"]),
            "API group order was preserved");

        var attached = mixedPage.Groups[0];
        Verify(attached.RoleState == RoleReadState.Attached, "a non-empty roles array is Attached");
        Verify(
            attached.RoleIds.SequenceEqual(["rbac_role_incident", "rbac_role_audit"]),
            "attached role IDs were preserved");

        var empty = mixedPage.Groups[1];
        Verify(
            empty.RoleState == RoleReadState.Empty && empty.RoleIds.Count == 0,
            "an empty roles array is Empty");

        var degraded = mixedPage.Groups[2];
        Verify(
            degraded.RoleState == RoleReadState.Degraded && degraded.RoleIds.Count == 0,
            "roles null is Degraded, not Empty");

        var blockedAudit = GroupRoleAudit.Evaluate(mixedPage.Groups);
        Verify(!blockedAudit.CanComplete, "a degraded group blocks the audit");
        Verify(
            blockedAudit.RetryGroupIds.SequenceEqual(["rbac_group_degraded"]),
            "the audit identifies the group that must be retried");

        var healthyAudit = GroupRoleAudit.Evaluate(mixedPage.Groups.Take(2));
        Verify(healthyAudit.CanComplete, "attached and intentionally empty groups can complete the audit");
        Verify(
            healthyAudit.AttachedGroups == 1
                && healthyAudit.EmptyGroups == 1
                && healthyAudit.TotalRoleAttachments == 2,
            "healthy audit counts remain distinct");
        Verify(
            mixedPage.HasMore && mixedPage.NextPage == "page_next",
            "pagination metadata was preserved without interpretation");

        VerifyThrows<JsonException>(
            () => RbacGroupPageParser.Parse(
                """{"data":[{"id":"rbac_group_missing","name":"Missing","source_type":"direct"}],"has_more":false,"next_page":null}"""),
            "a missing roles property is rejected as a contract error");
        VerifyThrows<JsonException>(
            () => RbacGroupPageParser.Parse(
                """{"data":[{"id":"rbac_group_scalar","name":"Scalar","source_type":"direct","roles":"rbac_role_wrong"}],"has_more":false,"next_page":null}"""),
            "a scalar roles value is rejected as a contract error");

        Console.WriteLine($"Verification passed: {_passed}/13");
    }

    private void Verify(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {description}");
        }

        _passed++;
        Console.WriteLine($"PASS: {description}");
    }

    private void VerifyThrows<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Verify(true, description);
            return;
        }

        throw new InvalidOperationException($"FAIL: {description}");
    }
}

internal static class RbacGroupPageParser
{
    public static RbacGroupPage Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The response must contain a data array.");
        }

        var groups = new List<RbacGroupRoles>();
        foreach (var element in data.EnumerateArray())
        {
            groups.Add(ParseGroup(element));
        }

        var hasMore = RequireBoolean(root, "has_more");
        var nextPage = ReadNullableString(root, "next_page");
        return new RbacGroupPage(groups, hasMore, nextPage);
    }

    private static RbacGroupRoles ParseGroup(JsonElement element)
    {
        var id = RequireString(element, "id");
        var name = RequireString(element, "name");
        var sourceType = RequireString(element, "source_type");

        if (sourceType is not ("direct" or "scim"))
        {
            throw new JsonException($"Group '{id}' has an unsupported source_type.");
        }

        if (!element.TryGetProperty("roles", out var roles))
        {
            throw new JsonException($"Group '{id}' is missing the required roles property.");
        }

        if (roles.ValueKind == JsonValueKind.Null)
        {
            return new RbacGroupRoles(id, name, sourceType, RoleReadState.Degraded, []);
        }

        if (roles.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Group '{id}' roles must be an array or null.");
        }

        var roleIds = new List<string>();
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(role.GetString()))
            {
                throw new JsonException($"Group '{id}' contains an invalid role ID.");
            }

            roleIds.Add(role.GetString()!);
        }

        var state = roleIds.Count == 0 ? RoleReadState.Empty : RoleReadState.Attached;
        return new RbacGroupRoles(id, name, sourceType, state, roleIds);
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"The {propertyName} property must be a non-empty string.");
        }

        return property.GetString()!;
    }

    private static bool RequireBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException($"The {propertyName} property must be a Boolean.");
        }

        return property.GetBoolean();
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new JsonException($"The response is missing {propertyName}.");
        }

        return property.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => property.GetString(),
            _ => throw new JsonException($"The {propertyName} property must be a string or null."),
        };
    }
}

internal static class GroupRoleAudit
{
    public static GroupRoleAuditResult Evaluate(IEnumerable<RbacGroupRoles> groups)
    {
        var snapshot = groups.ToArray();
        var retryGroupIds = snapshot
            .Where(group => group.RoleState == RoleReadState.Degraded)
            .Select(group => group.Id)
            .ToArray();

        return new GroupRoleAuditResult(
            CanComplete: retryGroupIds.Length == 0,
            AttachedGroups: snapshot.Count(group => group.RoleState == RoleReadState.Attached),
            EmptyGroups: snapshot.Count(group => group.RoleState == RoleReadState.Empty),
            TotalRoleAttachments: snapshot.Sum(group => group.RoleIds.Count),
            RetryGroupIds: retryGroupIds);
    }
}

internal enum RoleReadState
{
    Attached,
    Empty,
    Degraded,
}

internal sealed record RbacGroupRoles(
    string Id,
    string Name,
    string SourceType,
    RoleReadState RoleState,
    IReadOnlyList<string> RoleIds);

internal sealed record RbacGroupPage(
    IReadOnlyList<RbacGroupRoles> Groups,
    bool HasMore,
    string? NextPage);

internal sealed record GroupRoleAuditResult(
    bool CanComplete,
    int AttachedGroups,
    int EmptyGroups,
    int TotalRoleAttachments,
    IReadOnlyList<string> RetryGroupIds);
