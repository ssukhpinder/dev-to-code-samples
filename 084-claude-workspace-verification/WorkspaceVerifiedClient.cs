using System.Net.Http.Headers;
using System.Text;

internal sealed class WorkspaceVerifiedClient(HttpClient httpClient)
{
    private const string WorkspaceHeader = "anthropic-workspace-id";

    public async Task<string> PostMessageAsync(
        string targetWorkspaceId,
        string authorizedWorkspaceId,
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceId(targetWorkspaceId, "target");
        ValidateWorkspaceId(authorizedWorkspaceId, "authorized");

        if (!string.Equals(targetWorkspaceId, authorizedWorkspaceId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Target workspace '{targetWorkspaceId}' is not the independently " +
                $"authorized workspace '{authorizedWorkspaceId}'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(WorkspaceHeader, targetWorkspaceId);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        // Authentication failures can legitimately omit the workspace header.
        // Preserve the HTTP failure instead of replacing it with a misleading
        // workspace-validation error.
        response.EnsureSuccessStatusCode();

        var actualWorkspaceId = ReadSingleHeader(response.Headers, WorkspaceHeader);
        ValidateWorkspaceId(actualWorkspaceId, "response");

        if (!string.Equals(actualWorkspaceId, authorizedWorkspaceId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Claude API resolved workspace '{actualWorkspaceId}', " +
                $"but the request was authorized for '{authorizedWorkspaceId}'.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string ReadSingleHeader(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            throw new InvalidDataException(
                $"Successful Claude API response omitted {name}.");
        }

        var materialized = values.ToArray();
        if (materialized.Length != 1)
        {
            throw new InvalidDataException(
                $"Successful Claude API response returned {materialized.Length} {name} values.");
        }

        return materialized[0];
    }

    private static void ValidateWorkspaceId(string value, string source)
    {
        const string Prefix = "wrkspc_";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) ||
            value.Length == Prefix.Length)
        {
            throw new InvalidDataException(
                $"The {source} workspace ID is not a wrkspc_-prefixed identifier.");
        }

        foreach (var character in value.AsSpan(Prefix.Length))
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                throw new InvalidDataException(
                    $"The {source} workspace ID is not a wrkspc_-prefixed identifier.");
            }
        }
    }
}
