from __future__ import annotations

import json
from collections.abc import Callable, Mapping, Sequence
from copy import deepcopy
from dataclasses import dataclass
from typing import Any, TypeAlias

Item: TypeAlias = dict[str, Any]
Response: TypeAlias = Mapping[str, Any]
Send: TypeAlias = Callable[[list[Item]], Response]
Tool: TypeAlias = Callable[..., Any]


class ContinuationError(RuntimeError):
    """Raised when a response cannot be continued safely."""


class TurnLimitExceeded(ContinuationError):
    """Raised when a continuation never reaches a final message."""


@dataclass(frozen=True)
class LoopResult:
    message: Item
    input_items: list[Item]
    turns: int


def run_stateless_loop(
    send: Send,
    tools: Mapping[str, Tool],
    initial_input: Sequence[Mapping[str, Any]],
    *,
    max_turns: int = 8,
) -> LoopResult:
    """Run an offline-compatible `store: false` continuation loop."""

    if max_turns < 1:
        raise ValueError("max_turns must be at least 1")

    input_items = [deepcopy(dict(item)) for item in initial_input]
    known_programs = _program_call_ids(input_items)

    for turn in range(1, max_turns + 1):
        response = send(deepcopy(input_items))
        status = response.get("status")
        if status != "completed":
            raise ContinuationError(
                f"Response ended with status {status!r}; refusing to continue"
            )

        raw_output = response.get("output")
        if not isinstance(raw_output, list):
            raise ContinuationError("Response output must be a list")

        output = _copy_and_validate_items(raw_output)
        current_programs = _program_call_ids(output)
        visible_programs = known_programs | current_programs
        _validate_links(output, visible_programs)

        # Replay every item in provider order before adding local tool outputs.
        input_items.extend(output)
        known_programs.update(current_programs)

        calls = [item for item in output if item["type"] == "function_call"]
        if calls:
            input_items.extend(_run_calls(calls, tools))
            continue

        message = next((item for item in output if item["type"] == "message"), None)
        if message is not None:
            return LoopResult(
                message=deepcopy(message),
                input_items=deepcopy(input_items),
                turns=turn,
            )

        # A completed program_output can arrive before the final message.

    raise TurnLimitExceeded(f"No final message after {max_turns} turns")


def message_text(message: Mapping[str, Any]) -> str:
    """Return the first output-text part from a provider-shaped message."""

    content = message.get("content")
    if not isinstance(content, list):
        return ""

    for part in content:
        if isinstance(part, Mapping) and part.get("type") == "output_text":
            text = part.get("text")
            return text if isinstance(text, str) else ""
    return ""


def _copy_and_validate_items(raw_items: list[Any]) -> list[Item]:
    items: list[Item] = []
    for index, raw_item in enumerate(raw_items):
        if not isinstance(raw_item, Mapping):
            raise ContinuationError(f"Output item {index} must be an object")
        item = deepcopy(dict(raw_item))
        if not isinstance(item.get("type"), str):
            raise ContinuationError(f"Output item {index} is missing a type")
        items.append(item)
    return items


def _program_call_ids(items: Sequence[Mapping[str, Any]]) -> set[str]:
    result: set[str] = set()
    for item in items:
        if item.get("type") != "program":
            continue
        call_id = item.get("call_id")
        if not isinstance(call_id, str) or not call_id:
            raise ContinuationError("A program item needs a non-empty call_id")
        result.add(call_id)
    return result


def _validate_links(items: Sequence[Item], program_ids: set[str]) -> None:
    for item in items:
        item_type = item["type"]
        if item_type == "function_call":
            _require_string(item, "call_id", "function_call")
            _require_string(item, "name", "function_call")
            _require_string(item, "arguments", "function_call")
            caller = item.get("caller")
            if caller is not None:
                if not isinstance(caller, Mapping):
                    raise ContinuationError("function_call caller must be an object")
                if caller.get("type") != "program":
                    raise ContinuationError(
                        "function_call caller must have type 'program'"
                    )
                caller_id = caller.get("caller_id")
                if caller_id not in program_ids:
                    raise ContinuationError(
                        f"function_call references unknown program {caller_id!r}"
                    )
        elif item_type == "program_output":
            call_id = _require_string(item, "call_id", "program_output")
            if call_id not in program_ids:
                raise ContinuationError(
                    f"program_output references unknown program {call_id!r}"
                )
            if item.get("status") not in {"completed", "incomplete"}:
                raise ContinuationError(
                    "program_output status must be 'completed' or 'incomplete'"
                )


def _run_calls(calls: Sequence[Item], tools: Mapping[str, Tool]) -> list[Item]:
    outputs: list[Item] = []
    for call in calls:
        name = call["name"]
        implementation = tools.get(name)
        if implementation is None:
            raise ContinuationError(f"Unknown tool {name!r}")

        try:
            arguments = json.loads(call["arguments"])
        except json.JSONDecodeError as error:
            raise ContinuationError(f"Invalid arguments for {name!r}") from error
        if not isinstance(arguments, dict):
            raise ContinuationError(f"Arguments for {name!r} must decode to an object")

        result = implementation(**arguments)
        output: Item = {
            "type": "function_call_output",
            "call_id": call["call_id"],
            "output": json.dumps(result, separators=(",", ":"), sort_keys=True),
        }
        if call.get("caller") is not None:
            output["caller"] = deepcopy(call["caller"])
        outputs.append(output)
    return outputs


def _require_string(item: Mapping[str, Any], field: str, item_type: str) -> str:
    value = item.get(field)
    if not isinstance(value, str) or not value:
        raise ContinuationError(f"{item_type} needs a non-empty {field}")
    return value
