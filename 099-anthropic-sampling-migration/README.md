# Find removed Anthropic sampling parameters before runtime

Anthropic Python SDK v1 removed `temperature`, `top_p`, and `top_k` from
Messages method signatures. An upgraded application can therefore import
successfully and then raise `TypeError` only when a stale call path runs.

This sample provides a small AST-based preflight for direct keyword arguments
and local dictionary expansions. The included before fixture fails with three
precise findings; the migrated fixture passes. The verifier also inspects the
installed SDK signatures, so the demonstration stays tied to the real stable
package rather than a hand-written mock.

## Prerequisites

- Python 3.10 or later
- Network access only for the initial package installation

No Claude account, API key, paid request, model call, or runtime network access
is required. Do not put a real credential in this folder. A live application
should read a placeholder such as `ANTHROPIC_API_KEY` from its secret store.

## Setup

From this folder on Windows PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\python -m pip install --requirement requirements-dev.txt
```

On macOS or Linux, use `.venv/bin/python` for the remaining commands.

## Run the migration scan

The old fixture intentionally returns exit code `1`:

```powershell
.\.venv\Scripts\python scan_sampling_kwargs.py fixtures/before.py
```

Expected summary:

```text
SCAN FAILED: 3 removed sampling parameter(s) found
```

The scanner reports `temperature` and `top_k` as direct arguments and finds
`top_p` inside the local `request_options` dictionary. It ignores the unrelated
`room_temperature` application variable.

After removing the three arguments from current-model calls, the migrated
fixture passes:

```powershell
.\.venv\Scripts\python scan_sampling_kwargs.py fixtures/after.py
```

Expected output:

```text
SCAN PASSED: no removed Anthropic sampling parameters found
```

## Verify deterministically

```powershell
.\.venv\Scripts\python -m compileall -q .
.\.venv\Scripts\python -m ruff format --check .
.\.venv\Scripts\python -m ruff check .
.\.venv\Scripts\pyright --pythonpath .\.venv\Scripts\python.exe `
  scan_sampling_kwargs.py test_scan_sampling_kwargs.py verify.py
.\.venv\Scripts\python -m unittest -v
.\.venv\Scripts\python verify.py
.\.venv\Scripts\python -m pip check
.\.venv\Scripts\python -m pip_audit --requirement requirements.txt
```

The verifier pins `anthropic==1.2.0`, checks the real synchronous and
asynchronous method signatures, requires the old fixture to fail with exactly
three findings, and requires the new fixture to pass. Its final line is:

```text
VERIFIED 8/8 without an API key or network request
```

## Limitations and when not to use it

This is a migration preflight, not a Python type checker. It recognizes
attribute chains ending in Anthropic-style `messages` methods, but static syntax
alone cannot prove the receiver's type. It follows local literal dictionaries;
dictionaries built by functions, loaded from configuration, or mutated later
need a typed request model or a separate runtime schema check. Batch request
`params` also require their own nested-data validation.

For current models, remove these sampling settings. Anthropic's v1 migration
guide documents `extra_body` only as an escape hatch for an older model that
still honors a sampling parameter. Do not move every stale setting into
`extra_body` mechanically, because that bypasses the generated method signature
that caught the migration issue in the first place.
