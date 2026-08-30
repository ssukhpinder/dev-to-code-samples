# MCP Python SDK deprecation warnings in strict tests

## Problem

MCP Python SDK 2.1.1 emits visible runtime warnings for deprecated protocol
features. A blanket pytest warnings-as-errors rule can raise that warning inside
a tool handler. The SDK then converts the exception into a normal MCP tool error
result, so a test that only checks that the call returned can miss the warning
boundary it meant to enforce.

This sample compares that blanket policy with the SDK documentation's targeted
filter. It also shows how to assert the warning explicitly with pytest.

## Prerequisites

- Python 3.10 or later
- [uv](https://docs.astral.sh/uv/)

No account, credential, model call, external MCP host, database, or open network
port is required. There are no credential placeholders because the verifier runs
entirely in process. Dependency installation and auditing may contact the
configured package and advisory services.

## Setup and verification

Install the exact locked dependencies:

~~~bash
uv sync --all-groups
~~~

Run formatting, linting, type checking, tests, and the deterministic verifier:

~~~bash
uv lock --check
uv run ruff format --check .
uv run ruff check .
uv run mypy warning_contract.py verify.py test_warning_contract.py
uv run python -m compileall -q warning_contract.py verify.py test_warning_contract.py
uv run pytest -q
uv run python verify.py
uv run pip-audit --local --progress-spinner off
~~~

The pytest configuration keeps all other warnings fatal while restoring this
warning category to its normal visible behavior:

~~~toml
[tool.pytest.ini_options]
filterwarnings = [
    "error",
    "default::mcp.MCPDeprecationWarning",
]
~~~

The focused regression test uses pytest.warns(MCPDeprecationWarning) around the
deprecated call. That proves the warning is still emitted without changing the
tool result into an application-level failure.

## Expected behavior

The final command reports:

~~~text
[PASS] MCPDeprecationWarning is visible by default
[PASS] targeted handling captures the warning
[PASS] targeted handling keeps the tool successful
[PASS] blanket error handling emits no warning record
[PASS] blanket error handling becomes a tool error result
[PASS] tool error result reports execution failure
6/6 checks passed
~~~

The sample pins mcp==2.1.1 and deliberately calls the deprecated logging
helper once. With targeted warning handling, the call returns tool completed
and the warning remains observable. With a blanket error filter, no warning
record reaches the test and the call instead returns
Error executing tool legacy_log.

## Limitations

This is a warning-boundary test, not a recommendation to keep deprecated
logging. New code should use standard Python logging or OpenTelemetry, as the
MCP migration guidance recommends. The in-process client does not exercise
stdio, Streamable HTTP, authentication, or proxy behavior. Recheck the SDK's
deprecation guide before carrying the exact assertions to another version.

Primary references:

- [MCP Python SDK v2 migration guide](https://py.sdk.modelcontextprotocol.io/v2/migration/)
- [MCP 2026-07-28 final release](https://blog.modelcontextprotocol.io/posts/2026-07-28/)
- [mcp 2.1.1 on PyPI](https://pypi.org/project/mcp/2.1.1/)
