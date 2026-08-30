# MCP C# task polling stuck-loop guard

## Problem

An MCP Tasks server can remain in `input_required` while returning the same input-request key on every poll. A client should not prompt for that request repeatedly, and it should not poll forever when the server makes no progress.

The stable MCP C# Tasks extension handles both cases in `CallToolWithPollingAsync`. It deduplicates input-request keys, counts consecutive polls with no new key, sends a best-effort `tasks/cancel`, and throws `McpException` when `maxConsecutiveStuckPolls` is reached. The default limit is 60; this verifier sets it to 3 so the failure is immediate and deterministic.

## Prerequisites and dependencies

- .NET 10 SDK
- `ModelContextProtocol.Extensions.Tasks` 2.2.0, restored from NuGet
- PowerShell 5.1 or later for `verify.ps1`

No MCP host, model account, browser, credential, paid API, database, open port, clock, or randomness is required. There are no credential placeholders because the sample has no authenticated integration. Restore and the vulnerability audit may contact configured NuGet sources; the built verifier makes no network call.

## Setup and verification

From this folder, run the exact validation sequence:

```powershell
dotnet restore .\McpStuckTaskPolling.csproj --nologo
dotnet format .\McpStuckTaskPolling.csproj --verify-no-changes --no-restore
dotnet build .\McpStuckTaskPolling.csproj --configuration Release --no-restore --nologo
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
dotnet list .\McpStuckTaskPolling.csproj package --include-transitive
dotnet list .\McpStuckTaskPolling.csproj package --vulnerable --include-transitive
```

The in-memory transport advertises MCP `2026-07-28`, creates one task, and then returns the same `approval` input key forever. The handler declines it once. With a limit of 3, the helper observes one poll with new input plus three stuck polls, cancels once, and throws.

Expected verifier output:

```text
PASS: stuck poll guard raised McpException
PASS: repeated input key was presented once
PASS: threshold 3 stopped polling after 4 tasks/get calls
PASS: input response was sent once
PASS: best-effort tasks/cancel was sent once
PASS: repeated output was byte-for-byte deterministic
Summary: 6/6 passed
```

## Limits and production choices

This sample isolates the client-side stuck-poll behavior. It does not run an HTTP server, open the example URL, test sampling or roots input, authenticate a client, or prove that cancellation stopped remote work. MCP cancellation is cooperative and eventually consistent, so production code must still tolerate a late task state.

Choose the threshold with the poll interval and normal human-response latency in mind. A value that is too low can cancel a slow but healthy task; a value that is too high can hide a server that never advances. Also log task IDs and state transitions without logging elicitation answers or secrets.

See the official [C# SDK Tasks guide](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tasks/tasks.html), the [stable 2.2.0 package](https://www.nuget.org/packages/ModelContextProtocol.Extensions.Tasks/2.2.0), and the [MCP 2026-07-28 release notes](https://blog.modelcontextprotocol.io/posts/2026-07-28/) for the underlying contracts.
