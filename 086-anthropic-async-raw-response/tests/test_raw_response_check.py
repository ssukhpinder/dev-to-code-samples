from raw_response_check import run


def test_async_raw_response_requires_awaited_parse() -> None:
    assert run() == "PASS: 7/7 async raw-response checks"
