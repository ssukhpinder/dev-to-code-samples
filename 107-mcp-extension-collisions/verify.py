"""Run deterministic extension-registration checks and one valid in-memory request."""

import asyncio
from collections.abc import Callable

from mcp import Client
from mcp.client import advertise

from extension_contract import (
    EXTENSION_ID,
    METHOD,
    SearchParams,
    SearchRequest,
    SearchResult,
    build_core_method_binding,
    build_duplicate_server,
    build_unreachable_binding,
    build_valid_server,
)

Check = tuple[str, Callable[[], bool]]


def capture_value_error(action: Callable[[], object]) -> str:
    """Return a construction-time ValueError message, or fail the verifier."""
    try:
        action()
    except ValueError as error:
        return str(error)
    raise AssertionError("expected ValueError")


async def verify() -> None:
    """Prove invalid extension registrations fail before request handling starts."""
    server = build_valid_server()

    async with Client(server, extensions=[advertise(EXTENSION_ID)]) as client:
        request = SearchRequest(params=SearchParams(query="mcp"))
        result = await client.session.send_request(request, SearchResult)

    duplicate_error = capture_value_error(build_duplicate_server)
    core_error = capture_value_error(build_core_method_binding)
    versions_error = capture_value_error(build_unreachable_binding)

    checks: list[Check] = [
        ("unique vendor method starts normally", lambda: result.items == ["mcp-0", "mcp-1"]),
        ("typed request keeps the vendor method", lambda: request.method == METHOD),
        (
            "duplicate method fails during server construction",
            lambda: METHOD in duplicate_error and "already registered" in duplicate_error.lower(),
        ),
        (
            "core MCP method cannot be claimed",
            lambda: "tools/list" in core_error and "core" in core_error.lower(),
        ),
        (
            "empty protocol version set is rejected",
            lambda: "protocol_versions" in versions_error and "empty" in versions_error.lower(),
        ),
    ]

    failures = []
    for label, check in checks:
        passed = check()
        print(f"[{'PASS' if passed else 'FAIL'}] {label}")
        if not passed:
            failures.append(label)

    if failures:
        raise SystemExit(f"{len(failures)} check(s) failed: {', '.join(failures)}")

    print(f"{len(checks)}/{len(checks)} checks passed")


if __name__ == "__main__":
    asyncio.run(verify())
