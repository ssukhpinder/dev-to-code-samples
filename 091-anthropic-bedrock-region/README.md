# Anthropic Python SDK Bedrock region resolution

This sample verifies how Anthropic Python SDK v1 resolves the AWS region for `AnthropicBedrock`. It exercises the explicit argument, both documented environment variables, an AWS profile supplied through a fake `boto3` session, and the fail-fast path when no region exists.

## Problem

Anthropic Python SDK v1 no longer logs a warning and silently falls back to `us-east-1`. `AnthropicBedrock()` now raises `ValueError` during construction when it cannot resolve a region. An upgrade can therefore fail before the first request if deployment configuration relied on the old fallback.

## Prerequisites

- Python 3.10 or later
- Network access only for the initial package install

No Anthropic account, AWS account, credential, model call, or runtime network access is required. The sample uses a fake `boto3` module for the profile-resolution case and never sends an HTTP request.

## Setup

```bash
python -m venv .venv
.venv\Scripts\python -m pip install --upgrade pip
.venv\Scripts\python -m pip install -e ".[dev]"
```

On macOS or Linux, replace `.venv\Scripts\python` with `.venv/bin/python`.

## Run and test

```bash
.venv\Scripts\python bedrock_region_check.py
.venv\Scripts\python -m pytest -q
.venv\Scripts\python -m ruff format --check .
.venv\Scripts\python -m ruff check .
```

Expected verifier output:

```text
PASS explicit argument: eu-west-1
PASS AWS_REGION: ca-central-1
PASS AWS_DEFAULT_REGION: ap-southeast-2
PASS AWS profile: us-west-2
PASS missing region: rejected before HTTP
PASS: 5/5 Bedrock region checks
```

The important production fix is to make ownership explicit:

```python
from anthropic import AnthropicBedrock

client = AnthropicBedrock(aws_region="ca-central-1")
```

Passing `aws_region` in application configuration is the clearest choice when one deployment must always use one region. `AWS_REGION`, `AWS_DEFAULT_REGION`, or a configured `aws_profile` are useful when platform configuration owns region selection.

## Deterministic verification

The verifier removes ambient region and Bedrock base-URL variables before each scenario. It also replaces `boto3` with a tiny in-memory fake for the profile case and disables it for the missing-region case. This keeps the result independent of the developer machine's AWS configuration.

The test suite checks the selected region and generated Bedrock base URL for every successful path. It also checks the documented `ValueError` message for the missing-region path.

## Limitations

This sample checks constructor-time region resolution in Anthropic Python SDK 1.2.0. It does not validate IAM credentials, Bedrock model availability, inference profiles, account permissions, retries, or live request signing. Run a controlled integration check in the intended AWS account after the offline contract passes.

## References

- [Anthropic Python SDK v1 migration guide](https://github.com/anthropics/anthropic-sdk-python/blob/main/MIGRATION.md#bedrock-a-region-is-now-required)
- [Anthropic Python SDK 1.2.0 release](https://github.com/anthropics/anthropic-sdk-python/releases/tag/v1.2.0)
- [Claude Platform release notes](https://platform.claude.com/docs/en/release-notes/overview)
