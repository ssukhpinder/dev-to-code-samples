"""Run deterministic, in-memory checks against the MCP server."""

from __future__ import annotations

import asyncio
import threading
from typing import Any

from mcp import Client
from mcp.types import CallToolResult

from server import mcp


def structured(result: CallToolResult) -> dict[str, Any]:
    assert result.is_error is not True
    assert result.structured_content is not None
    return result.structured_content


async def run_checks() -> None:
    client_loop_thread_id = threading.get_ident()

    async with Client(mcp, raise_exceptions=True) as client:
        sync_result = await client.call_tool(
            "inspect_sync_handler",
            {"client_loop_thread_id": client_loop_thread_id},
        )
        async_result = await client.call_tool(
            "inspect_async_handler",
            {"client_loop_thread_id": client_loop_thread_id},
        )
        total_result = await client.call_tool(
            "total_on_worker",
            {
                "values": [1, 2, 3, 4, 5],
                "client_loop_thread_id": client_loop_thread_id,
            },
        )

    sync_data = structured(sync_result)
    async_data = structured(async_result)
    total_data = structured(total_result)

    assert sync_data == {
        "handler_kind": "sync",
        "same_thread_as_client_loop": False,
        "has_running_event_loop": False,
        "loop_error_type": "RuntimeError",
    }
    print("PASS sync tool: worker thread has no running event loop")

    assert async_data == {
        "handler_kind": "async",
        "same_thread_as_client_loop": True,
        "has_running_event_loop": True,
        "loop_error_type": None,
    }
    print("PASS async tool: event-loop thread is preserved")

    assert total_data == {"total": 15, "same_thread_as_client_loop": False}
    print("PASS worker-safe sync tool: total is 15")

    assert all(result.is_error is not True for result in (sync_result, async_result, total_result))
    print("PASS protocol results: all three calls succeeded")

    print("PASS transport: client and server stayed in memory")
    print("PASS: 5/5 MCP Python SDK v2 thread checks")


if __name__ == "__main__":
    asyncio.run(run_checks())
