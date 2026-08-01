# OpenAI Programmatic Tool Calling loop

Companion sample for **OpenAI Programmatic Tool Calling: Stop Dropping `program` Items**.

## Problem

With `store: false`, a Responses API continuation must replay every item from
`response.output`. A loop that keeps only `function_call` items can discard a
`program`, reasoning state, or `program_output`. A nested function result must
also preserve both the function `call_id` and its `caller` link to the program.

This sample models that contract with plain Python dictionaries and deterministic
fixtures. It never calls the OpenAI API.

## Prerequisites

- Python 3.11 or newer
- No API key, package install, network access, or paid model request

## Setup

Clone the repository and enter this folder. There are no runtime dependencies:

```shell
cd 016-openai-ptc-loop
python --version
```

## Run the demonstration

```shell
python demo.py
```

Expected behavior:

```text
Inventory check complete.
Turns: 2
Replayed: user -> reasoning -> program -> future_item -> function_call -> function_call_output -> program_output -> message
```

The first fake response pauses a program for a client-owned function. The loop
replays the complete response, runs the local function, and appends a
`function_call_output` with the original `call_id` and `caller`. The second fake
response supplies `program_output` and the final assistant message.

## Run verification

```shell
python -m compileall -q src tests demo.py
python -m unittest discover -s tests -v
python demo.py
```

The tests cover complete ordered replay, an unknown future item, caller-link
validation, program-output validation, incomplete top-level responses, and the
maximum-turn guard.

## Files

- `src/ptc_loop/loop.py` contains the stateless continuation loop.
- `demo.py` runs a two-turn offline fixture.
- `tests/test_loop.py` provides deterministic regression tests.

## Limitations

This is an orchestration-contract sample, not a replacement OpenAI SDK. It uses
provider-shaped dictionaries so it can run offline. Production code should use
the current SDK types, validate tool arguments and authorization, handle tool
exceptions deliberately, and make side-effecting calls safely retryable.

The replay strategy is specifically for `store: false`. Stored responses should
continue with `previous_response_id` and new function outputs rather than replaying
the full history.
