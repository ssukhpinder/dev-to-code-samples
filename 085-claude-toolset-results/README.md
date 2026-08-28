# Claude browser toolset result correlation

This sample prevents a rejected Claude Messages follow-up after a browser or
computer toolset member runs successfully. Member `tool_use` blocks carry both
`name` and `toolset_name`; their matching `tool_result` blocks must echo the
same `toolset_name`.

The fixture deliberately includes two tools named `screenshot`: one browser
toolset member and one custom tool. The dispatcher keys handlers by the
`(toolset_name, name)` pair, then a local preflight catches omitted and
mismatched correlation fields before a request is sent.

The contract follows Anthropic's current documentation:

- [Tool reference: client toolsets](https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-reference#client-toolsets)
- [Handle tool calls](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls#handling-results-from-client-tools)

## Prerequisites

- .NET 10 SDK
- No Claude account, API key, browser, or paid model call

If you adapt the pattern to a live application, keep the real credential out of
source control and use an environment variable such as
`ANTHROPIC_API_KEY=replace-with-your-key`. This sample does not read that
variable.

## Setup and verification

From this folder, run:

```powershell
dotnet restore
dotnet format ClaudeToolsetResults.csproj --verify-no-changes
dotnet build ClaudeToolsetResults.csproj -c Release --no-restore
dotnet run --project ClaudeToolsetResults.csproj -c Release --no-build
dotnet list ClaudeToolsetResults.csproj package
dotnet list ClaudeToolsetResults.csproj package --vulnerable --include-transitive
```

The verifier prints ten passing checks and a correctly correlated user-message
content array. The browser member result includes a deterministic PNG image
block and `"toolset_name": "browser"`; the custom result contains text and
omits `toolset_name` even though both calls use the member name `screenshot`.

## What the verifier proves

- dispatch uses both `toolset_name` and `name`, preventing same-name collisions;
- a successful browser `screenshot` serializes as a PNG image content block;
- browser member results echo the original `toolset_name`;
- custom-tool results do not gain a toolset field;
- one result matches each `tool_use_id`;
- omitted or changed member `toolset_name` values fail local preflight.

The program uses fixed in-memory JSON and has no network, credential, clock,
randomness, locale, or filesystem dependency. Repeated runs should produce the
same output bytes.

## Limitations

This is a narrow correlation verifier, not a browser driver or a replacement
for Anthropic's request validation. It does not call the Claude API, reproduce
the remote rejection, execute batch actions, or cover the text, image, and
`browser_state` rules for every browser member. Add those checks around your
real executor as its responsibilities grow.
