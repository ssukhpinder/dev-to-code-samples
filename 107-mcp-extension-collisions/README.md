# MCP Python SDK extension method collisions

## Problem

An MCP extension can add vendor request methods, but those methods still share one dispatch
table. A duplicate method name, an attempted override of a core MCP method, or a binding with no
supported protocol version should stop startup. Letting any of those configurations use
last-write-wins behavior would make the active handler depend on registration order.

This sample pins MCP Python SDK 2.1.1 and verifies that invalid method ownership fails during
construction, before a server starts accepting requests. It also sends one valid vendor request
through the SDK's in-memory client to prove the non-conflicting path works.

## Prerequisites

- Python 3.10 or later
- [uv](https://docs.astral.sh/uv/)

No account, credential, model call, external MCP host, database, or open network port is required.
Dependency installation and the vulnerability audit may contact configured package services;
the verifier and tests run entirely in process.

## Setup and verification

From this folder, install the locked runtime and development dependencies:

```bash
uv sync --all-groups
```

Run the complete deterministic validation:

```bash
uv lock --check
uv run ruff format --check .
uv run ruff check .
uv run mypy extension_contract.py verify.py test_extension_contract.py
uv run python -m compileall -q extension_contract.py verify.py test_extension_contract.py
uv run python -m unittest -v
uv run python verify.py
uv run pip-audit
```

The verifier checks one successful method plus three construction-time failures. The unit tests
cover the same registration boundary independently.

## Expected behavior

```text
[PASS] unique vendor method starts normally
[PASS] typed request keeps the vendor method
[PASS] duplicate method fails during server construction
[PASS] core MCP method cannot be claimed
[PASS] empty protocol version set is rejected
5/5 checks passed
```

The unit test command reports four passing tests, and `pip-audit` reports no known vulnerabilities
for the installed dependency set.

## Limitations

This sample tests construction and in-memory dispatch, not stdio, Streamable HTTP, authentication,
extension result claims, or notification bindings. The exact exception text is an SDK 2.1.1
contract test; prefer the exception type and the offending method when supporting several SDK
versions.
