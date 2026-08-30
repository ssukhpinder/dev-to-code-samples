"""Regression tests for strict MCP deprecation-warning handling."""

import asyncio
import warnings

import pytest
from mcp import MCPDeprecationWarning

from warning_contract import (
    EXPECTED_WARNING,
    call_legacy_tool,
    probe_warning_policy,
)


def test_targeted_assertion_observes_warning_without_breaking_tool() -> None:
    with pytest.warns(MCPDeprecationWarning, match="logging capability is deprecated"):
        result = asyncio.run(call_legacy_tool())

    assert not result.is_error


def test_documented_filter_order_keeps_warning_nonfatal() -> None:
    result = asyncio.run(probe_warning_policy("always"))

    assert result.warning_messages == (EXPECTED_WARNING,)
    assert not result.is_error


def test_blanket_warning_error_becomes_tool_error_result() -> None:
    with warnings.catch_warnings():
        warnings.simplefilter("error", MCPDeprecationWarning)
        result = asyncio.run(call_legacy_tool())

    assert result.is_error
    assert [block.text for block in result.content if hasattr(block, "text")] == [
        "Error executing tool legacy_log"
    ]
