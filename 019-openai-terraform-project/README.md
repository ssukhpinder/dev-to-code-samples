# Official OpenAI Terraform provider project sample

This sample shows how to create an OpenAI project with the official `openai/openai` Terraform provider while keeping the Admin API key out of configuration and source control.

## Problem

Manually creating OpenAI projects makes configuration drift hard to review. Putting an Admin API key directly in HCL creates a different problem: the credential can reach Git history, logs, or a Terraform plan file.

The configuration keeps credentials in `OPENAI_ADMIN_KEY`, pins the first stable provider release, and uses a mocked provider for deterministic tests that do not call OpenAI.

## Prerequisites

- Terraform 1.7 or later (`mock_provider` is used by the test)
- An OpenAI organization and Admin API key only when you intentionally run a real plan or apply

## Setup and offline validation

```shell
terraform init
terraform fmt -check -recursive
terraform validate
terraform test
```

`terraform init` downloads `openai/openai` v1.0.0 and creates `.terraform.lock.hcl`. Commit that lock file. The remaining commands format-check the files, validate the provider schema, and run one mocked plan. The test should finish with one passing run and makes no OpenAI API request.

## Plan and apply against OpenAI

Export the Admin API key in your shell. Do not put it in a `.tf`, `.tfvars`, or committed `.env` file.

PowerShell:

```powershell
$env:OPENAI_ADMIN_KEY = "replace-with-an-admin-api-key"
terraform plan -var 'project_name=terraform-managed-demo'
```

Bash:

```shell
export OPENAI_ADMIN_KEY="replace-with-an-admin-api-key"
terraform plan -var='project_name=terraform-managed-demo'
```

Review the plan before running `terraform apply`. A successful apply creates one OpenAI project and prints its project ID.

## Expected behavior

- `terraform validate` reports a valid configuration.
- `terraform test` reports one passed run without credentials or network calls to OpenAI.
- A real plan proposes one `openai_project` resource.
- A real apply changes the target OpenAI organization.

## Limitations

This sample covers project creation only. It does not configure users, service accounts, role assignments, rate limits, spend alerts, or remote state. Real `plan`, `apply`, and `destroy` operations require careful account targeting and review; they were deliberately not run for this sample.
