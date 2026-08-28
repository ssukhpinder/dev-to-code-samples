"""Verify the Anthropic Python SDK v1 async raw-response contract offline."""

import asyncio
import inspect

import httpx2
from anthropic import AsyncAnthropic


def response_payload() -> dict[str, object]:
    return {
        "id": "msg_offline_raw_response",
        "type": "message",
        "role": "assistant",
        "model": "claude-sonnet-5",
        "content": [{"type": "text", "text": "parsed after await"}],
        "stop_reason": "end_turn",
        "stop_sequence": None,
        "usage": {"input_tokens": 1, "output_tokens": 3},
    }


async def run_verification() -> list[str]:
    checks: list[str] = []
    observed_paths: list[str] = []

    def verify(condition: bool, message: str) -> None:
        if not condition:
            raise AssertionError(message)
        checks.append(message)

    async def handler(request: httpx2.Request) -> httpx2.Response:
        observed_paths.append(request.url.path)
        return httpx2.Response(
            200,
            request=request,
            headers={"request-id": "req_offline_raw_response"},
            json=response_payload(),
        )

    http_client = httpx2.AsyncClient(transport=httpx2.MockTransport(handler))
    client = AsyncAnthropic(
        api_key="offline-placeholder",
        base_url="https://api.anthropic.com",
        http_client=http_client,
    )

    try:
        raw_response = await client.messages.with_raw_response.create(
            model="claude-sonnet-5",
            max_tokens=8,
            messages=[{"role": "user", "content": "ping"}],
        )

        verify(observed_paths == ["/v1/messages"], "one mocked Messages request")
        verify(raw_response.status_code == 200, "status metadata stays synchronous")
        verify(
            raw_response.headers["request-id"] == "req_offline_raw_response",
            "headers stay synchronous",
        )

        pending_message = raw_response.parse()
        verify(inspect.isawaitable(pending_message), "parse() returns an awaitable")

        try:
            _ = pending_message.content
        except AttributeError:
            legacy_access_failed = True
        else:
            legacy_access_failed = False

        verify(
            legacy_access_failed,
            "legacy message access fails before awaiting parse()",
        )

        message = await pending_message
        verify(message.type == "message", "await parse() returns a Message")
        verify(
            message.content[0].text == "parsed after await",
            "parsed content matches the mocked response",
        )
    finally:
        await client.close()

    return checks


def run() -> str:
    checks = asyncio.run(run_verification())
    return f"PASS: {len(checks)}/7 async raw-response checks"


if __name__ == "__main__":
    print(run())
