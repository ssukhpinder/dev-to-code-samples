from __future__ import annotations

import sys
import unittest
from copy import deepcopy
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parents[1] / "src"))

from ptc_loop import (
    ContinuationError,
    TurnLimitExceeded,
    message_text,
    run_stateless_loop,
)

PROGRAM = {
    "type": "program",
    "id": "prog_1",
    "call_id": "call_prog_1",
    "code": "text(await tools.lookup({key: 'x'}))",
    "fingerprint": "fixture",
}


class StatelessLoopTests(unittest.TestCase):
    def test_replays_every_item_and_preserves_caller(self) -> None:
        first_output = [
            {"type": "reasoning", "id": "rs_1", "encrypted_content": "fixture"},
            PROGRAM,
            {"type": "future_item", "id": "future_1", "payload": {"keep": True}},
            {
                "type": "function_call",
                "id": "fc_1",
                "call_id": "call_lookup_1",
                "name": "lookup",
                "arguments": '{"key":"x"}',
                "caller": {"type": "program", "caller_id": "call_prog_1"},
            },
        ]
        second_output = [
            {
                "type": "program_output",
                "id": "prog_out_1",
                "call_id": "call_prog_1",
                "result": '{"value":7}',
                "status": "completed",
            },
            {
                "type": "message",
                "id": "msg_1",
                "role": "assistant",
                "content": [{"type": "output_text", "text": "Done."}],
            },
        ]
        responses = iter(
            [
                {"status": "completed", "output": first_output},
                {"status": "completed", "output": second_output},
            ]
        )
        received: list[list[dict[str, object]]] = []

        def send(items: list[dict[str, object]]) -> dict[str, object]:
            received.append(deepcopy(items))
            return deepcopy(next(responses))

        result = run_stateless_loop(
            send,
            {"lookup": lambda key: {"key": key, "value": 7}},
            [{"type": "user", "content": "Look up x."}],
        )

        self.assertEqual(message_text(result.message), "Done.")
        self.assertEqual(result.turns, 2)
        self.assertEqual(received[1][1:5], first_output)
        tool_output = received[1][5]
        self.assertEqual(tool_output["type"], "function_call_output")
        self.assertEqual(tool_output["call_id"], "call_lookup_1")
        self.assertEqual(
            tool_output["caller"],
            {"type": "program", "caller_id": "call_prog_1"},
        )
        self.assertEqual(
            [item["type"] for item in result.input_items],
            [
                "user",
                "reasoning",
                "program",
                "future_item",
                "function_call",
                "function_call_output",
                "program_output",
                "message",
            ],
        )

    def test_rejects_orphaned_program_caller(self) -> None:
        response = {
            "status": "completed",
            "output": [
                {
                    "type": "function_call",
                    "call_id": "call_1",
                    "name": "lookup",
                    "arguments": "{}",
                    "caller": {"type": "program", "caller_id": "missing"},
                }
            ],
        }

        with self.assertRaisesRegex(ContinuationError, "unknown program 'missing'"):
            run_stateless_loop(lambda _: response, {"lookup": dict}, [])

    def test_rejects_orphaned_program_output(self) -> None:
        response = {
            "status": "completed",
            "output": [
                {
                    "type": "program_output",
                    "call_id": "missing",
                    "result": "{}",
                    "status": "completed",
                }
            ],
        }

        with self.assertRaisesRegex(ContinuationError, "unknown program 'missing'"):
            run_stateless_loop(lambda _: response, {}, [])

    def test_rejects_incomplete_top_level_response(self) -> None:
        response = {"status": "incomplete", "output": []}

        with self.assertRaisesRegex(ContinuationError, "status 'incomplete'"):
            run_stateless_loop(lambda _: response, {}, [])

    def test_bounds_turns_without_a_final_message(self) -> None:
        response = {"status": "completed", "output": []}

        with self.assertRaisesRegex(TurnLimitExceeded, "after 2 turns"):
            run_stateless_loop(lambda _: response, {}, [], max_turns=2)

    def test_rejects_non_object_tool_arguments(self) -> None:
        response = {
            "status": "completed",
            "output": [
                {
                    "type": "function_call",
                    "call_id": "call_1",
                    "name": "lookup",
                    "arguments": "[]",
                }
            ],
        }

        with self.assertRaisesRegex(ContinuationError, "must decode to an object"):
            run_stateless_loop(lambda _: response, {"lookup": dict}, [])


if __name__ == "__main__":
    unittest.main()
