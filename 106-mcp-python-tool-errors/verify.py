"""Run deterministic checks against the server through the in-memory MCP client."""

import asyncio
from collections.abc import Callable

from mcp import MCPError
from mcp.client import Client
from mcp.types import INVALID_PARAMS, CallToolResult, TextContent

from server import server

Check = tuple[str, Callable[[], bool]]


def text_from(result: CallToolResult) -> str:
    """Join text blocks without assuming every content block is text."""
    return "\n".join(block.text for block in result.content if isinstance(block, TextContent))


async def verify() -> None:
    """Exercise every error channel and fail if its client-visible contract changes."""
    async with Client(server) as client:
        success = await client.call_tool("find_book", {"title": "Dune"})
        recoverable = await client.call_tool("find_book", {"title": "Solaris"})
        unexpected = await client.call_tool("crash_lookup", {"title": "internal-id"})

        protocol_error: MCPError | None = None
        try:
            await client.call_tool("require_catalog_ready", {})
        except MCPError as error:
            protocol_error = error

    success_text = text_from(success)
    recoverable_text = text_from(recoverable)
    unexpected_text = text_from(unexpected)

    checks: list[Check] = [
        ("successful call is not marked as an error", lambda: success.is_error is False),
        ("successful content is preserved", lambda: success_text == "Dune is available."),
        ("ToolError becomes an error result", lambda: recoverable.is_error is True),
        (
            "ToolError gives the model a concrete retry",
            lambda: "No book titled 'Solaris'. Try Dune." in recoverable_text,
        ),
        (
            "unexpected exception text is sanitized",
            lambda: (
                unexpected.is_error is True
                and unexpected_text == "Error executing tool crash_lookup"
                and "private_inventory" not in unexpected_text
            ),
        ),
        ("MCPError leaves the result channel", lambda: protocol_error is not None),
        (
            "MCPError preserves its protocol code and message",
            lambda: (
                protocol_error is not None
                and protocol_error.error.code == INVALID_PARAMS
                and protocol_error.error.message == "Catalog is not initialized."
            ),
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
