# MCP Python SDK 2.1 tool error channels

## Problem

An MCP tool can fail through two different wire-level channels. A recoverable execution
failure belongs in a tool result with `is_error=True`, where the calling model can read it.
A request-level failure belongs in a JSON-RPC error handled by the client. Mixing them up can
hide an actionable retry hint or make a failed operation look successful.

This sample verifies how MCP Python SDK 2.1.1 handles four cases:

- a normal result;
- an anticipated `ToolError`;
- an unexpected exception whose internal detail must be sanitized; and
- an `MCPError` that preserves its JSON-RPC code and message.

## Prerequisites

- Python 3.10 or later
- [uv](https://docs.astral.sh/uv/)

No account, credential, model call, external server, or database is required. Dependency
installation may contact the configured Python package index. Verification runs entirely
in process after installation.

## Setup and verification

From this folder, install the locked dependencies:

```bash
uv sync --all-groups
```

Run the same deterministic checks used for validation:

```bash
uv lock --check
uv run ruff format --check .
uv run ruff check .
uv run mypy server.py verify.py
uv run python -m compileall -q server.py verify.py
uv run python verify.py
```

The final command reports seven passing checks. It proves that the deliberate tool failure
keeps its retry hint, the unexpected exception does not expose the simulated internal path,
and the protocol failure arrives as `MCPError` with code `-32602`.

## Expected behavior

```text
[PASS] successful call is not marked as an error
[PASS] successful content is preserved
[PASS] ToolError becomes an error result
[PASS] ToolError gives the model a concrete retry
[PASS] unexpected exception text is sanitized
[PASS] MCPError leaves the result channel
[PASS] MCPError preserves its protocol code and message
7/7 checks passed
```

## Limitations

The sample uses the SDK's in-memory client, so it does not test stdio, Streamable HTTP,
authentication, proxies, or transport timeouts. It pins `mcp==2.1.1`; check the SDK's current
error-handling documentation before applying the exact assertions to another version.
