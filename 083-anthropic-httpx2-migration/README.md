# Anthropic Python SDK HTTPX2 migration

This sample verifies an offline Anthropic Messages request after migrating the SDK-facing transport from `httpx` to `httpx2`.

## Problem

After upgrading to Anthropic Python SDK v1, a mock or instrumentation library that only patches legacy `httpx` can stop observing SDK requests. Code that passes a legacy `httpx.Client` to `Anthropic(http_client=...)` is also incompatible with the new HTTP layer.

## Prerequisites

- Python 3.10 or later
- Network access only for the initial package install

No Anthropic account or API key is needed. The value in the source is an intentional non-secret placeholder, and `httpx2.MockTransport` handles the request locally.

## Setup and run

```bash
python -m venv .venv
.venv\Scripts\python -m pip install --upgrade pip
.venv\Scripts\python -m pip install -e . pytest
.venv\Scripts\python migration_check.py
.venv\Scripts\python -m pytest -q
```

On macOS/Linux, replace `.venv\Scripts\python` with `.venv/bin/python`.

## Expected behavior

The script prints `PASS: SDK request was intercepted by httpx2.MockTransport`. The test makes one synthetic `/v1/messages` request and asserts that the handler received an `httpx2.Request`; it never contacts Anthropic.

## What to change in an application

Use `import httpx2 as httpx` for SDK-facing clients, transports, type annotations, and mocks. If an application must preserve legacy `import httpx` integrations temporarily, call `httpx2.alias_httpx()` at the very beginning of the application entry point, before any `httpx` import. Libraries should not call that process-wide alias on behalf of callers.

## Limitations

This is a transport-contract check, not an end-to-end API test. It does not validate authentication, model availability, quota, proxy trust configuration, streaming, or third-party instrumentation compatibility. Test those in your own controlled environment after migrating.

## References

- [Anthropic Python SDK v1 migration guide](https://github.com/anthropics/anthropic-sdk-python/blob/main/MIGRATION.md)
- [Anthropic Python SDK](https://github.com/anthropics/anthropic-sdk-python)
