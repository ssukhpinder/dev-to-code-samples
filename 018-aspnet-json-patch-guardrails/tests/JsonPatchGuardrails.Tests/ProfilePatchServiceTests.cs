using System.Text.Json;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Xunit;

namespace JsonPatchGuardrails.Tests;

public sealed class ProfilePatchServiceTests
{
    private static readonly Profile Existing =
        new("Original", "America/Edmonton", IsAdmin: false);

    [Fact]
    public void Allows_replace_on_an_explicitly_editable_field()
    {
        var patch = Parse(
            """
            [{ "op": "replace", "path": "/displayName", "value": "New name" }]
            """);

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.True(result.Succeeded);
        Assert.Equal("New name", result.Profile.DisplayName);
        Assert.False(result.Profile.IsAdmin);
        Assert.Equal("Original", Existing.DisplayName);
    }

    [Fact]
    public void Rejects_a_security_sensitive_path_before_mutation()
    {
        var patch = Parse(
            """
            [{ "op": "replace", "path": "/isAdmin", "value": true }]
            """);

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Contains(result.Errors, error => error.Contains("not patchable"));
    }

    [Fact]
    public void Rejects_copy_even_when_both_paths_are_editable()
    {
        var patch = Parse(
            """
            [{ "op": "copy", "from": "/displayName", "path": "/timeZone" }]
            """);

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Contains(result.Errors, error => error.Contains("not allowed"));
    }

    [Fact]
    public void Rejects_documents_over_the_operation_limit()
    {
        var operations = string.Join(
            ',',
            Enumerable.Repeat(
                "{ \"op\": \"test\", \"path\": \"/timeZone\", \"value\": \"America/Edmonton\" }",
                9));
        var patch = Parse($"[{operations}]");

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Contains(result.Errors, error => error.Contains("at most 8"));
    }

    [Fact]
    public void Rejects_a_null_operation_instead_of_throwing()
    {
        var patch = Parse("[null]");

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Contains(result.Errors, error => error.Contains("cannot be null"));
    }

    [Fact]
    public void Keeps_the_original_when_an_operation_fails()
    {
        var patch = Parse(
            """
            [
              { "op": "test", "path": "/displayName", "value": "Someone else" },
              { "op": "replace", "path": "/displayName", "value": "Should not persist" }
            ]
            """);

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Equal("Original", Existing.DisplayName);
    }

    [Fact]
    public void Keeps_the_original_when_the_result_breaks_a_business_rule()
    {
        var patch = Parse(
            """
            [{ "op": "replace", "path": "/timeZone", "value": "Not/AZone" }]
            """);

        var result = ProfilePatchService.TryApply(Existing, patch);

        Assert.False(result.Succeeded);
        Assert.Same(Existing, result.Profile);
        Assert.Contains(result.Errors, error => error.Contains("not supported"));
    }

    private static JsonPatchDocument<EditableProfile> Parse(string json) =>
        JsonSerializer.Deserialize<JsonPatchDocument<EditableProfile>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("The patch fixture could not be parsed.");
}
