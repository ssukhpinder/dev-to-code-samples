from __future__ import annotations

import inspect
import subprocess
import sys
from pathlib import Path

import anthropic
from anthropic.resources.messages.messages import AsyncMessages, Messages

from scan_sampling_kwargs import REMOVED_PARAMETERS, scan_paths

ROOT = Path(__file__).resolve().parent
BEFORE = ROOT / "fixtures" / "before.py"
AFTER = ROOT / "fixtures" / "after.py"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def run_scanner(path: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(ROOT / "scan_sampling_kwargs.py"), str(path)],
        check=False,
        capture_output=True,
        text=True,
    )


def main() -> int:
    checks = 0

    require(anthropic.__version__ == "1.2.0", "expected anthropic 1.2.0")
    checks += 1
    print(f"PASS {checks}/8 stable SDK version is anthropic {anthropic.__version__}")

    for resource in (Messages, AsyncMessages):
        for method_name in ("create", "stream", "parse"):
            parameters = inspect.signature(getattr(resource, method_name)).parameters
            require(
                REMOVED_PARAMETERS.isdisjoint(parameters),
                f"{resource.__name__}.{method_name} still exposes a removed parameter",
            )
    checks += 1
    print("PASS 2/8 sync and async message signatures omit all three parameters")

    before_findings, before_errors = scan_paths([BEFORE])
    require(not before_errors, f"before fixture scan errors: {before_errors}")
    require(
        [item.parameter for item in before_findings]
        == ["top_p", "temperature", "top_k"],
        f"unexpected before findings: {before_findings}",
    )
    checks += 1
    print("PASS 3/8 before fixture exposes temperature, top_p, and top_k")

    require(
        {item.source for item in before_findings} == {"keyword", "expanded dictionary"},
        "before fixture must exercise direct and expanded parameters",
    )
    checks += 1
    print("PASS 4/8 scanner covers direct and dictionary-expanded arguments")

    after_findings, after_errors = scan_paths([AFTER])
    require(not after_errors, f"after fixture scan errors: {after_errors}")
    require(
        not after_findings, f"migrated fixture still has findings: {after_findings}"
    )
    checks += 1
    print("PASS 5/8 migrated fixture contains no removed sampling parameters")

    before_process = run_scanner(BEFORE)
    require(before_process.returncode == 1, "before scanner must exit with code 1")
    require("SCAN FAILED: 3" in before_process.stdout, before_process.stdout)
    checks += 1
    print("PASS 6/8 failing scan returns a CI-friendly nonzero exit")

    after_process = run_scanner(AFTER)
    require(after_process.returncode == 0, "after scanner must exit with code 0")
    require("SCAN PASSED" in after_process.stdout, after_process.stdout)
    checks += 1
    print("PASS 7/8 clean scan returns zero")

    require(
        "room_temperature" in AFTER.read_text(encoding="utf-8"),
        "unrelated application terminology should remain in the migrated fixture",
    )
    checks += 1
    print("PASS 8/8 unrelated temperature terminology remains untouched")
    print("VERIFIED 8/8 without an API key or network request")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
