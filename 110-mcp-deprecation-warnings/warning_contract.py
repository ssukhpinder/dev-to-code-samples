"""Exercise one deprecated MCP call under two warning policies."""

from __future__ import annotations

import warnings
from dataclasses import dataclass
from typing import Literal

from mcp import Client, MCPDeprecationWarning
from mcp.server.mcpserver import Context, MCPServer
from mcp.types import CallToolResult, TextContent

EXPECTED_WARNING = "The logging capability is deprecated as of 2026-07-28 (SEP-2577)."

server = MCPServer("deprecation-warning-contract", log_level="CRITICAL")


@server.tool()
async def legacy_log(ctx: Context) -> str:
    """Call one deprecated helper so the warning boundary is observable."""
    await ctx.session.send_log_message("info", "legacy diagnostic")
    return "tool completed"


@dataclass(frozen=True)
class ProbeResult:
    """The externally visible result of one warning-policy probe."""

    is_error: bool
    warning_messages: tuple[str, ...]
    text_content: tuple[str, ...]


async def call_legacy_tool() -> CallToolResult:
    """Invoke the sample tool through the SDK's in-process client."""
    async with Client(server) as client:
        return await client.call_tool("legacy_log", {})


async def probe_warning_policy(
    action: Literal["always", "error"],
) -> ProbeResult:
    """Capture what the caller sees when the warning filter changes."""
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter(action, MCPDeprecationWarning)
        result = await call_legacy_tool()

    return ProbeResult(
        is_error=bool(result.is_error),
        warning_messages=tuple(str(item.message) for item in caught),
        text_content=tuple(
            block.text for block in result.content if isinstance(block, TextContent)
        ),
    )
