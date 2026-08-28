from migration_check import run


def test_sdk_request_uses_httpx2_mock_transport() -> None:
    assert run() == "PASS: SDK request was intercepted by httpx2.MockTransport"
