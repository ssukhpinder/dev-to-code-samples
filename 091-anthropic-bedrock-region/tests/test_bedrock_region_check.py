from __future__ import annotations

import pytest

from bedrock_region_check import verify_region_resolution


@pytest.fixture(scope="module")
def results() -> list[str]:
    return verify_region_resolution()


def test_all_resolution_paths_are_checked(results: list[str]) -> None:
    assert len(results) == 5


@pytest.mark.parametrize(
    "expected",
    [
        "PASS explicit argument: eu-west-1",
        "PASS AWS_REGION: ca-central-1",
        "PASS AWS_DEFAULT_REGION: ap-southeast-2",
        "PASS AWS profile: us-west-2",
        "PASS missing region: rejected before HTTP",
    ],
)
def test_resolution_result_is_present(results: list[str], expected: str) -> None:
    assert expected in results
