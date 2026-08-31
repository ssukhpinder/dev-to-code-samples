# OpenAI API-key usage and cost reconciliation

The OpenAI organization Usage API and Costs API can both group daily buckets by `api_key_id`. The two result sets still need a careful join: a key can appear on only one side, and the API can return a null key ID for unattributed activity.

This .NET 10 console sample reads synthetic, paginated fixtures and builds a full-outer reconciliation report keyed by UTC day plus API-key ID. It keeps token counts separate from billed amounts, preserves null IDs as `<unattributed>`, and fails closed on dimensions that would make the report misleading.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- No NuGet packages, OpenAI credentials, network access, or paid API calls

## Run and verify

From this folder:

```powershell
dotnet restore
dotnet format OpenAiApiKeyUsageCosts.csproj --verify-no-changes --no-restore
dotnet build OpenAiApiKeyUsageCosts.csproj -c Release --no-restore
dotnet run --project OpenAiApiKeyUsageCosts.csproj -c Release --no-build
```

The executable follows two usage pages and two cost pages, then prints six rows: four matched, one usage-only, and one cost-only. It finishes with:

```text
PASS: 9/9 API-key usage-cost checks passed
```

The checks also prove that a missing continuation cursor, duplicate day/key dimension, mixed currency, or non-daily bucket is rejected. Running the program again produces the same report.

## Adapt the fixtures to API responses

Request both resources with `bucket_width=1d` and `group_by=api_key_id`, and keep following `next_page` while `has_more` is true:

```text
GET /v1/organization/usage/completions?start_time=...&bucket_width=1d&group_by=api_key_id
GET /v1/organization/costs?start_time=...&bucket_width=1d&group_by=api_key_id
```

When adding live HTTP retrieval, read an organization Admin API key from an environment variable such as `OPENAI_ADMIN_KEY`; never put its value in source or fixtures. The checked-in program intentionally does not read that variable or make network requests.

The response shapes and pagination fields are documented in OpenAI's [completions usage](https://developers.openai.com/api/reference/python/resources/admin/subresources/organization/subresources/usage/methods/completions) and [costs](https://developers.openai.com/api/reference/python/resources/admin/subresources/organization/subresources/usage/methods/costs) references. API-key grouping was announced in the [OpenAI API changelog](https://developers.openai.com/api/docs/changelog).

## Limits

This is a reconciliation pattern, not a pricing calculator. A Costs API bucket can include line items beyond completion-token usage, so do not derive or validate billed cost by multiplying tokens by a model price. `api_key_id` is also an aggregate operational dimension, not per-request attribution. Preserve usage-only, cost-only, and null-key rows so gaps stay visible.

The cursor-to-fixture mapping is deliberately fixed for deterministic offline verification. A live client should request each opaque cursor from the API while retaining the same missing-cursor and repeated-cursor guards.
