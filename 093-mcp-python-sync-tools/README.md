# MCP Python SDK v2 sync handler worker threads

This sample reproduces the thread boundary introduced for synchronous tools in MCP Python SDK v2. It proves that a `def` tool runs on an AnyIO worker thread without a running `asyncio` event loop, while an `async def` tool stays on the event-loop thread.

## Problem

MCP Python SDK v1 called synchronous tools, resources, and prompts inline on the event loop. Version 2 moves those handlers, along with resolver functions, to `anyio.to_thread.run_sync()` so blocking synchronous work does not stall other requests.

That concurrency improvement changes the execution contract. A synchronous handler that calls `asyncio.get_running_loop()` now raises `RuntimeError`. Thread-local state and non-thread-safe objects created on the event-loop thread also need review.

## Prerequisites and setup

- Python 3.10 or later
- Network access for the package install and vulnerability audit

Create a virtual environment and install the pinned runtime and development dependencies:

```bash
python -m venv .venv
.venv\Scripts\python -m pip install --upgrade "pip==26.2"
.venv\Scripts\python -m pip install -e ".[dev]"
```

On macOS or Linux, replace `.venv\Scripts\python` with `.venv/bin/python`.

No account, credential, paid request, model call, child process, open port, or verifier/test network access is required. The verifier uses the SDK's in-memory `Client(mcp)` connection. Package installation and `pip-audit` may contact configured package and advisory services. If you adapt the sample to a protected HTTP deployment, keep the real token outside source control and use a placeholder such as `MCP_AUTH_TOKEN=replace-me` in local documentation.

## Run and test

```bash
.venv\Scripts\python verify.py
.venv\Scripts\python -m pytest -q
.venv\Scripts\python -m ruff format --check .
.venv\Scripts\python -m ruff check .
.venv\Scripts\python -m pip check
.venv\Scripts\python -m pip_audit
```

Expected verifier output:

```text
PASS sync tool: worker thread has no running event loop
PASS async tool: event-loop thread is preserved
PASS worker-safe sync tool: total is 15
PASS protocol results: all three calls succeeded
PASS transport: client and server stayed in memory
PASS: 5/5 MCP Python SDK v2 thread checks
```

`inspect_sync_handler` catches the documented `RuntimeError` only so the verifier can inspect it. The production fix for loop-bound code is to declare the tool `async def` and use `await`. A tool that performs ordinary synchronous work can remain `def`; `total_on_worker` demonstrates that path.

## Deterministic verification

The verifier passes the in-process client's event-loop thread ID into each tool. It asserts that the synchronous tools run on a different thread, the asynchronous tool stays on the original thread, and only the asynchronous tool has a running event loop. It prints booleans and fixed totals rather than unstable thread IDs or timings.

The tests call the registered tools through the real MCP client/server protocol path in memory. They do not call the Python functions directly, so a future change to handler dispatch will fail the contract.

## Limitations

This sample verifies MCP Python SDK 2.1.1 on the `asyncio` backend. It does not measure throughput, configure AnyIO's worker limit, prove a third-party object is thread-safe, or test an HTTP/stdio transport. Worker threads also do not make CPU-heavy Python code parallel; use the process or native-code strategy appropriate to that workload.

## References

- [MCP Python SDK v2 migration: sync handlers run on a worker thread](https://github.com/modelcontextprotocol/python-sdk/blob/main/docs/migration.md#sync-handler-functions-now-run-on-a-worker-thread)
- [MCP Python SDK in-memory testing](https://github.com/modelcontextprotocol/python-sdk/blob/main/docs/get-started/testing.md)
- [MCP Python SDK 2.1.1 release](https://github.com/modelcontextprotocol/python-sdk/releases/tag/v2.1.1)
