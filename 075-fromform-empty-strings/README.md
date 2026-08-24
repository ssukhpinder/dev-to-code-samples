# ASP.NET Core 10 `[FromForm]` empty strings

## Problem

Before ASP.NET Core 10, a Minimal API endpoint that bound a complex `[FromForm]` object could reject an empty string for a nullable value-type property. A browser form such as `Quantity=&ShipDate=` produced HTTP 400 even though both destination properties were nullable.

ASP.NET Core 10 aligns complex form-object binding with other Minimal API form binding: empty strings for nullable value types become `null`. Nonblank malformed input still produces HTTP 400.

This sample runs the same endpoint and fixed request matrix on `net9.0` and `net10.0`. It checks blank, valid, and malformed form values without an external service.

## Prerequisites

- A stable .NET 10 SDK. `global.json` accepts the latest installed .NET 10 feature band and rejects prerelease SDKs.
- The .NET 9 and .NET 10 ASP.NET Core runtimes for the side-by-side comparison. The .NET 10 run works independently if only the current runtime is installed.
- A machine that permits a process to listen on a dynamic loopback port.
- No account, credential, database, container, browser, or external service.

## Setup and validation

Run these commands from this folder:

```powershell
dotnet --version
dotnet restore .\FormBindingSample\FormBindingSample.csproj
dotnet format .\FormBindingSample\FormBindingSample.csproj --verify-no-changes --no-restore
dotnet build .\FormBindingSample\FormBindingSample.csproj -c Release --no-restore
dotnet run --project .\FormBindingSample\FormBindingSample.csproj -f net9.0 -c Release --no-build
dotnet run --project .\FormBindingSample\FormBindingSample.csproj -f net10.0 -c Release --no-build
dotnet list .\FormBindingSample\FormBindingSample.csproj package --vulnerable --include-transitive
```

The `net9.0` run expects blank optional values to return HTTP 400. The `net10.0` run expects the same values to return HTTP 200 and bind as `null`. Both targets also prove that valid values survive binding and malformed nonblank input remains a 400.

Each run prints `PASS` for every check and ends with its target-specific result.

## The binding contract

The endpoint receives one complex form object:

```csharp
app.MapPost(
        "/orders",
        ([FromForm] OrderForm form) =>
            TypedResults.Ok(new BindingResult(form.Quantity, form.ShipDate)))
    .DisableAntiforgery();
```

The request matrix keeps blank and malformed input separate:

| Case | Form values | ASP.NET Core 9 | ASP.NET Core 10 |
| --- | --- | --- | --- |
| Blank | `Quantity=&ShipDate=` | 400 | 200, both `null` |
| Valid | `Quantity=7&ShipDate=2026-08-24` | 200 | 200 |
| Malformed | `Quantity=seven` | 400 | 400 |

That last row matters: .NET 10 does not turn every parse failure into `null`. Only an empty string gets the nullable treatment.

## Expected behavior and limitations

This is a Minimal API complex-object `[FromForm]` sample. MVC controllers have a different model-binding pipeline, so do not use this matrix as proof of a controller behavior change.

The endpoint disables antiforgery only because the executable is a local binding harness. A browser-facing application that accepts cookie-authenticated form posts should configure and validate antiforgery protection rather than copying that choice.

The requests stay on loopback, and the assigned port is intentionally omitted from output. The sample checks binding semantics, not HTML validation, localization, custom model binders, or application-level rules such as whether `Quantity` is required.
