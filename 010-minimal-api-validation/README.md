# Minimal API Built-in Validation (.NET 10)

.NET 10 gives minimal APIs the automatic model validation controllers have had
since `[ApiController]`: call `builder.Services.AddValidation()`, put
DataAnnotations on your DTOs and parameters, and invalid requests get a 400
`ValidationProblemDetails` before your handler ever runs.

The demo starts a minimal API, probes itself with `HttpClient`, and prints the
real status codes and response bodies for:

- a hand-written validation block (the "before" — kept runnable with `.DisableValidation()`)
- the same rules as attributes on a record, enforced automatically
- a `[Range]` attribute directly on a query parameter
- a cross-field rule via `IValidatableObject` (`SalePrice < Price`)
- the escape hatch: `.DisableValidation()` letting garbage through untouched

It also demonstrates the ordering nuance: `IValidatableObject.Validate` only
runs after all attribute validations pass, so cross-field errors arrive in a
second wave.

## Run it

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK. No external dependencies — `AddValidation` ships in
the shared framework.

📖 Article: _link added after publish_
