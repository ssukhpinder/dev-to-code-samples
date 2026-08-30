"""Run deterministic checks for MCP deprecation-warning handling."""

import asyncio
from collections.abc import Callable

from mcp import MCPDeprecationWarning

from warning_contract import EXPECTED_WARNING, probe_warning_policy

Check = tuple[str, Callable[[], bool]]


async def verify() -> None:
    """Compare a visible warning with a warning promoted to an exception."""
    visible = await probe_warning_policy("always")
    strict = await probe_warning_policy("error")

    checks: list[Check] = [
        (
            "MCPDeprecationWarning is visible by default",
            lambda: issubclass(MCPDeprecationWarning, UserWarning),
        ),
        (
            "targeted handling captures the warning",
            lambda: visible.warning_messages == (EXPECTED_WARNING,),
        ),
        (
            "targeted handling keeps the tool successful",
            lambda: not visible.is_error and visible.text_content == ("tool completed",),
        ),
        (
            "blanket error handling emits no warning record",
            lambda: strict.warning_messages == (),
        ),
        (
            "blanket error handling becomes a tool error result",
            lambda: strict.is_error,
        ),
        (
            "tool error result reports execution failure",
            lambda: strict.text_content == ("Error executing tool legacy_log",),
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
