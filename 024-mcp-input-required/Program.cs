using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
var protector = new ApprovalStateProtector(RandomNumberGenerator.GetBytes(32), clock);
var requestState = String.Empty;

var checks = new (string Name, Action Run)[]
{
    ("first call requests bound approval", () => requestState = CaptureRequestState(protector)),
    ("valid accepted confirmation proceeds", () => Expect("Deploy production", ApprovalFlow.Resolve(
        "production", "accept", ApprovalContent(confirm: true), requestState, protector))),
    ("only the named confirm field counts", () => Expect("Deployment not approved", ApprovalFlow.Resolve(
        "production", "accept", ApprovalContent(confirm: false, extra: true), requestState, protector))),
    ("decline and cancel stop", () => VerifyNegativeActions(requestState, protector)),
    ("retry without approval stops", () => Expect("Deployment not approved", ApprovalFlow.Resolve(
        "production", null, null, requestState, protector))),
    ("tampered state is rejected", () => Expect("Approval state invalid", ApprovalFlow.Resolve(
        "production", "accept", ApprovalContent(confirm: true), requestState + "x", protector))),
    ("expired state is rejected", () => VerifyExpiredState(clock, protector)),
};

var passed = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        passed++;
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {check.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{passed}/{checks.Length} checks passed");
return passed == checks.Length ? 0 : 1;

static string CaptureRequestState(ApprovalStateProtector protector)
{
    try
    {
        return ApprovalFlow.RequestApproval("production", protector);
    }
    catch (InputRequiredException exception)
    {
        if (!exception.Result.InputRequests!.ContainsKey("approval")
            || !protector.IsValid(exception.Result.RequestState, "production"))
        {
            throw new InvalidOperationException("The input-required result was not bound to this deployment.");
        }

        return exception.Result.RequestState!;
    }
}

static void VerifyNegativeActions(string requestState, ApprovalStateProtector protector)
{
    Expect("Deployment not approved", ApprovalFlow.Resolve(
        "production", "decline", null, requestState, protector));
    Expect("Deployment not approved", ApprovalFlow.Resolve(
        "production", "cancel", null, requestState, protector));
}

static void VerifyExpiredState(ManualTimeProvider clock, ApprovalStateProtector protector)
{
    var shortLived = protector.Protect("production", TimeSpan.FromSeconds(1));
    clock.Advance(TimeSpan.FromSeconds(1));
    Expect("Approval state invalid", ApprovalFlow.Resolve(
        "production", "accept", ApprovalContent(confirm: true), shortLived, protector));
}

static IDictionary<string, JsonElement> ApprovalContent(bool confirm, bool? extra = null)
{
    var content = new Dictionary<string, JsonElement>();
    if (extra is not null)
    {
        content["ignored"] = JsonSerializer.SerializeToElement(extra.Value);
    }

    content["confirm"] = JsonSerializer.SerializeToElement(confirm);
    return content;
}

static void Expect(string expected, string actual)
{
    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}

internal static class ApprovalFlow
{
    public static string RequestApproval(string environment, ApprovalStateProtector protector)
    {
        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["approval"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = $"Deploy to {environment}?",
                    RequestedSchema = new()
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["confirm"] = new ElicitRequestParams.BooleanSchema
                            {
                                Description = "Confirm this deployment",
                            },
                        },
                    },
                }),
            },
            requestState: protector.Protect(environment, TimeSpan.FromMinutes(5)));
    }

    public static string Resolve(
        string environment,
        string? action,
        IDictionary<string, JsonElement>? content,
        string? requestState,
        ApprovalStateProtector protector)
    {
        if (!protector.IsValid(requestState, environment))
        {
            return "Approval state invalid";
        }

        var confirmed = content?.TryGetValue("confirm", out var value) is true
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

        return action == "accept" && confirmed
            ? $"Deploy {environment}"
            : "Deployment not approved";
    }
}

[McpServerToolType]
internal sealed class DeploymentTools(ApprovalStateProtector protector)
{
    [McpServerTool]
    public string ConfirmDeployment(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        string environment)
    {
        if (context.Params!.RequestState is { } requestState)
        {
            if (context.Params.InputResponses?.TryGetValue("approval", out var response) is not true)
            {
                return ApprovalFlow.Resolve(environment, null, null, requestState, protector);
            }

            var result = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            return ApprovalFlow.Resolve(
                environment,
                result?.Action,
                result?.Content,
                requestState,
                protector);
        }

        if (context.Params.InputResponses is { Count: > 0 })
        {
            return "Approval state invalid";
        }

        if (!server.IsMrtrSupported)
        {
            return "This client cannot complete a multi-round-trip request.";
        }

        return ApprovalFlow.RequestApproval(environment, protector);
    }
}

internal sealed class ApprovalStateProtector(byte[] key, TimeProvider timeProvider)
{
    private readonly byte[] _key = key.Length >= 32
        ? key.ToArray()
        : throw new ArgumentException("Use at least 32 random bytes.", nameof(key));

    public string Protect(string environment, TimeSpan lifetime)
    {
        if (environment.IndexOfAny(['\r', '\n']) >= 0 || lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid approval-state input.");
        }

        var expires = timeProvider.GetUtcNow().Add(lifetime).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"v1\ndeploy\n{environment}\n{expires}");
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{ToBase64Url(payload)}.{ToBase64Url(signature)}";
    }

    public bool IsValid(string? state, string environment)
    {
        try
        {
            var parts = state?.Split('.') ?? [];
            if (parts.Length != 2)
            {
                return false;
            }

            var payload = FromBase64Url(parts[0]);
            var suppliedSignature = FromBase64Url(parts[1]);
            var expectedSignature = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                return false;
            }

            var fields = Encoding.UTF8.GetString(payload).Split('\n');
            return fields is ["v1", "deploy", var boundEnvironment, var expiresText]
                && StringComparer.Ordinal.Equals(boundEnvironment, environment)
                && Int64.TryParse(expiresText, out var expires)
                && timeProvider.GetUtcNow().ToUnixTimeSeconds() < expires;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
}
