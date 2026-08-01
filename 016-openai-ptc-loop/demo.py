from __future__ import annotations

import sys
from copy import deepcopy
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "src"))

from ptc_loop import message_text, run_stateless_loop


def main() -> None:
    responses = iter(
        [
            {
                "status": "completed",
                "output": [
                    {"type": "reasoning", "id": "rs_1", "encrypted_content": "fixture"},
                    {
                        "type": "program",
                        "id": "prog_1",
                        "call_id": "call_prog_1",
                        "code": "text(await tools.get_inventory({sku: 'sku_123'}))",
                        "fingerprint": "offline-fixture",
                    },
                    {"type": "future_item", "id": "future_1", "value": "preserve me"},
                    {
                        "type": "function_call",
                        "id": "fc_1",
                        "call_id": "call_inventory_1",
                        "name": "get_inventory",
                        "arguments": '{"sku":"sku_123"}',
                        "caller": {"type": "program", "caller_id": "call_prog_1"},
                    },
                ],
            },
            {
                "status": "completed",
                "output": [
                    {
                        "type": "program_output",
                        "id": "prog_out_1",
                        "call_id": "call_prog_1",
                        "result": '{"available_units":42,"sku":"sku_123"}',
                        "status": "completed",
                    },
                    {
                        "type": "message",
                        "id": "msg_1",
                        "role": "assistant",
                        "content": [
                            {"type": "output_text", "text": "Inventory check complete."}
                        ],
                    },
                ],
            },
        ]
    )

    def send(_input_items: list[dict[str, object]]) -> dict[str, object]:
        return deepcopy(next(responses))

    result = run_stateless_loop(
        send,
        {"get_inventory": lambda sku: {"sku": sku, "available_units": 42}},
        [{"type": "user", "content": "Check sku_123."}],
    )

    print(message_text(result.message))
    print(f"Turns: {result.turns}")
    print("Replayed: " + " -> ".join(item["type"] for item in result.input_items))


if __name__ == "__main__":
    main()
