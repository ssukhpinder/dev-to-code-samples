from __future__ import annotations

import os
import sys
from collections.abc import Iterator
from contextlib import contextmanager
from types import SimpleNamespace
from unittest.mock import patch

from anthropic import AnthropicBedrock

REGION_ENVIRONMENT_KEYS = (
    "ANTHROPIC_BEDROCK_BASE_URL",
    "AWS_BEARER_TOKEN_BEDROCK",
    "AWS_DEFAULT_REGION",
    "AWS_REGION",
)


@contextmanager
def isolated_region_environment(**values: str) -> Iterator[None]:
    """Temporarily remove ambient Bedrock-region inputs and apply explicit values."""
    missing = object()
    original: dict[str, str | object] = {
        key: os.environ.get(key, missing) for key in REGION_ENVIRONMENT_KEYS
    }

    for key in REGION_ENVIRONMENT_KEYS:
        os.environ.pop(key, None)
    os.environ.update(values)

    try:
        yield
    finally:
        for key in REGION_ENVIRONMENT_KEYS:
            os.environ.pop(key, None)
        for key, value in original.items():
            if value is not missing:
                os.environ[key] = str(value)


def resolve_client_region(
    *, aws_region: str | None = None, aws_profile: str | None = None
) -> tuple[str, str]:
    client = AnthropicBedrock(aws_region=aws_region, aws_profile=aws_profile)
    try:
        return client.aws_region, str(client.base_url)
    finally:
        client.close()


def expected_base_url(region: str) -> str:
    return f"https://bedrock-runtime.{region}.amazonaws.com"


def verify_region_resolution() -> list[str]:
    results: list[str] = []

    with isolated_region_environment(AWS_REGION="ca-central-1"):
        region, base_url = resolve_client_region(aws_region="eu-west-1")
        assert (region, base_url) == ("eu-west-1", expected_base_url("eu-west-1"))
        results.append("PASS explicit argument: eu-west-1")

    with isolated_region_environment(
        AWS_REGION="ca-central-1", AWS_DEFAULT_REGION="ap-southeast-2"
    ):
        region, base_url = resolve_client_region()
        assert (region, base_url) == ("ca-central-1", expected_base_url("ca-central-1"))
        results.append("PASS AWS_REGION: ca-central-1")

    with isolated_region_environment(AWS_DEFAULT_REGION="ap-southeast-2"):
        region, base_url = resolve_client_region()
        assert (region, base_url) == (
            "ap-southeast-2",
            expected_base_url("ap-southeast-2"),
        )
        results.append("PASS AWS_DEFAULT_REGION: ap-southeast-2")

    fake_boto3 = SimpleNamespace(
        Session=lambda *, profile_name: SimpleNamespace(
            region_name="us-west-2" if profile_name == "sample-profile" else None
        )
    )
    with isolated_region_environment(), patch.dict(sys.modules, {"boto3": fake_boto3}):
        region, base_url = resolve_client_region(aws_profile="sample-profile")
        assert (region, base_url) == ("us-west-2", expected_base_url("us-west-2"))
        results.append("PASS AWS profile: us-west-2")

    with isolated_region_environment(), patch.dict(sys.modules, {"boto3": None}):
        try:
            resolve_client_region()
        except ValueError as error:
            assert "No AWS region was provided" in str(error)
        else:
            raise AssertionError("AnthropicBedrock accepted a missing AWS region")
        results.append("PASS missing region: rejected before HTTP")

    return results


def main() -> None:
    results = verify_region_resolution()
    for result in results:
        print(result)
    print(f"PASS: {len(results)}/5 Bedrock region checks")


if __name__ == "__main__":
    main()
