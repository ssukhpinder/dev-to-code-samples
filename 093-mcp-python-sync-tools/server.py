"""MCP tools that make the Python SDK v2 thread boundary observable."""

from __future__ import annotations

import asyncio
import threading

from mcp.server.mcpserver import MCPServer
from pydantic import BaseModel


class ThreadReport(BaseModel):
    handler_kind: str
    same_thread_as_client_loop: bool
    has_running_event_loop: bool
    loop_error_type: str | None


class TotalReport(BaseModel):
    total: int
    same_thread_as_client_loop: bool


mcp = MCPServer("sync-handler-thread-boundary")


@mcp.tool()
def inspect_sync_handler(client_loop_thread_id: int) -> ThreadReport:
    """Show the worker-thread behavior of a synchronous v2 tool."""
    try:
        asyncio.get_running_loop()
    except RuntimeError as error:
        return ThreadReport(
            handler_kind="sync",
            same_thread_as_client_loop=threading.get_ident() == client_loop_thread_id,
            has_running_event_loop=False,
            loop_error_type=type(error).__name__,
        )

    return ThreadReport(
        handler_kind="sync",
        same_thread_as_client_loop=threading.get_ident() == client_loop_thread_id,
        has_running_event_loop=True,
        loop_error_type=None,
    )


@mcp.tool()
async def inspect_async_handler(client_loop_thread_id: int) -> ThreadReport:
    """Show that an asynchronous v2 tool stays on the event-loop thread."""
    asyncio.get_running_loop()
    return ThreadReport(
        handler_kind="async",
        same_thread_as_client_loop=threading.get_ident() == client_loop_thread_id,
        has_running_event_loop=True,
        loop_error_type=None,
    )


@mcp.tool()
def total_on_worker(values: list[int], client_loop_thread_id: int) -> TotalReport:
    """Run purely synchronous work without depending on an event loop."""
    return TotalReport(
        total=sum(values),
        same_thread_as_client_loop=threading.get_ident() == client_loop_thread_id,
    )
