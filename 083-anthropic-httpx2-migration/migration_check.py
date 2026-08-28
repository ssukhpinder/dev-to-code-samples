"""Run one mocked Messages request through the SDK's HTTPX2 transport."""

from anthropic import Anthropic
import httpx2


def response_payload() -> dict[str, object]:
    return {
        "id": "msg_offline_example",
        "type": "message",
        "role": "assistant",
        "model": "claude-sonnet-4-6",
        "content": [{"type": "text", "text": "transport reached"}],
        "stop_reason": "end_turn",
        "stop_sequence": None,
        "usage": {"input_tokens": 1, "output_tokens": 2},
    }


def run() -> str:
    observed: list[str] = []

    def handler(request: httpx2.Request) -> httpx2.Response:
        observed.append(f"{type(request).__module__}.{type(request).__name__}")
        assert request.url.path == "/v1/messages"
        return httpx2.Response(200, request=request, json=response_payload())

    transport = httpx2.MockTransport(handler)
    with httpx2.Client(transport=transport) as http_client:
        client = Anthropic(api_key="offline-placeholder", http_client=http_client)
        message = client.messages.create(
            model="claude-sonnet-4-6",
            max_tokens=8,
            messages=[{"role": "user", "content": "ping"}],
        )

    assert message.content[0].text == "transport reached"
    assert observed == ["httpx2.Request"]
    return "PASS: SDK request was intercepted by httpx2.MockTransport"


if __name__ == "__main__":
    print(run())
