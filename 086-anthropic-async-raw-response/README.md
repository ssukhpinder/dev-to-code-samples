# Anthropic Python SDK async raw response

This sample verifies the Anthropic Python SDK v1 contract for parsing an async raw response. It uses an in-memory HTTP transport, so the verification never calls the Claude API.

## Problem

After upgrading to Anthropic Python SDK v1, `await client.messages.with_raw_response.create(...)` still returns a raw response wrapper. On the async client, `parse()`, `read()`, `text()`, and `json()` are now coroutines. Code migrated from the legacy response class can call `response.parse()` without a second `await` and then try to use the coroutine as a Message.

## Prerequisites

- Python 3.10 or later
- Network access only for the initial package install

No Anthropic account or API key is needed. The source contains an intentional non-secret placeholder, and `httpx2.MockTransport` returns a fixed response in memory.

## Setup, run, and test

```bash
python -m venv .venv
.venv\Scripts\python -m pip install --upgrade pip
.venv\Scripts\python -m pip install . pytest
.venv\Scripts\python raw_response_check.py
.venv\Scripts\python -m pytest -q
```

On macOS/Linux, replace `.venv\Scripts\python` with `.venv/bin/python`.

## Expected behavior

The script prints `PASS: 7/7 async raw-response checks`, and pytest reports one passing test. The verifier confirms that status and headers remain synchronous metadata, `parse()` returns an awaitable, legacy attribute access fails before the second `await`, and the awaited parse returns the mocked Message.

The corrected pattern is:

```python
raw_response = await client.messages.with_raw_response.create(...)
print(raw_response.headers["request-id"])
message = await raw_response.parse()
```

## Limitations

This checks the SDK response contract, not the Claude API. It does not validate authentication, model access, quota, retries, streaming, proxy configuration, or response latency. Run a controlled integration check for those concerns with credentials managed outside source control.

## References

- [Anthropic Python SDK v1 migration guide](https://github.com/anthropics/anthropic-sdk-python/blob/main/MIGRATION.md#with_raw_response-returns-the-new-response-classes)
- [Anthropic Python SDK documentation](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/python)
