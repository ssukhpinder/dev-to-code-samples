from __future__ import annotations

import threading
from typing import Any

import pytest
from mcp import Client
from mcp.types import CallToolResult

from server import mcp


@pytest.fixture
def anyio_backend() -> str:
    return "asyncio"


def structured(result: CallToolResult) -> dict[str, Any]:
    assert result.is_error is not True
    assert result.structured_content is not None
    return result.structured_content


@pytest.mark.anyio
async def test_sync_handler_runs_on_worker_without_event_loop() -> None:
    client_thread_id = threading.get_ident()

    async with Client(mcp, raise_exceptions=True) as client:
        result = await client.call_tool(
            "inspect_sync_handler",
            {"client_loop_thread_id": client_thread_id},
        )

    assert structured(result) == {
        "handler_kind": "sync",
        "same_thread_as_client_loop": False,
        "has_running_event_loop": False,
        "loop_error_type": "RuntimeError",
    }


@pytest.mark.anyio
async def test_async_handler_stays_on_event_loop_thread() -> None:
    client_thread_id = threading.get_ident()

    async with Client(mcp, raise_exceptions=True) as client:
        result = await client.call_tool(
            "inspect_async_handler",
            {"client_loop_thread_id": client_thread_id},
        )

    assert structured(result) == {
        "handler_kind": "async",
        "same_thread_as_client_loop": True,
        "has_running_event_loop": True,
        "loop_error_type": None,
    }


@pytest.mark.anyio
async def test_sync_tool_can_do_worker_safe_work() -> None:
    client_thread_id = threading.get_ident()

    async with Client(mcp, raise_exceptions=True) as client:
        result = await client.call_tool(
            "total_on_worker",
            {"values": [8, 13, 21], "client_loop_thread_id": client_thread_id},
        )

    assert structured(result) == {
        "total": 42,
        "same_thread_as_client_loop": False,
    }
