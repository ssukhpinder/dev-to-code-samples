# .NET 10 JSON source generation reference handling

## Problem

A source-generated `System.Text.Json` context rejects a cyclic object graph by default. In .NET 10, `JsonSourceGenerationOptionsAttribute` can declare a `ReferenceHandler` directly, so the generated context can preserve shared references and cycles without a reflection fallback or call-site options.

This sample builds a manager/report cycle and verifies both sides of that contract:

- the default generated context rejects the cyclic graph at the depth limit;
- the preserve context emits `$id` and `$ref` metadata;
- deserialization recreates the cycle with reference identity intact; and
- reflection-based serialization is disabled by project configuration.

## Prerequisites

- .NET SDK 10.0.303 or a later stable .NET 10 SDK
- No external services or credentials

## Setup

```bash
cd 058-json-sourcegen-reference-handler
dotnet restore
```

The project has no NuGet package dependencies. `System.Text.Json` comes from the .NET 10 shared framework.

## Run and verify

```bash
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --vulnerable --include-transitive
```

The executable is also the deterministic verifier. A successful run reports eight checks and exits with code `0`. It first confirms that the default generated context rejects the cycle, then confirms that the preserve context emits reference metadata and restores this identity:

```text
ReferenceEquals(manager, manager.DirectReports[0].Manager) == true
```

The JSON includes `$id`, `$values`, and `$ref` metadata. Repeated runs with the same runtime and source produce the same output; the sample does not read the clock, environment, network, filesystem, locale, or random input.

## Key configuration

The reference policy is part of the generated context:

```csharp
[JsonSourceGenerationOptions(
    ReferenceHandler = JsonKnownReferenceHandler.Preserve,
    WriteIndented = true)]
[JsonSerializable(typeof(Employee))]
internal partial class PreserveGraphContext : JsonSerializerContext;
```

The project disables reflection fallback:

```xml
<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
```

## Limitations

`Preserve` changes the wire format by adding System.Text.Json reference metadata. Consumers that do not understand `$id`, `$ref`, and the `$values` collection wrapper may not accept it. It also does not preserve identity for value types or immutable/parameterized-constructor types, so this sample deliberately uses a mutable POCO that can be allocated before its relationships are populated. If a plain interoperable JSON shape matters more than identity, map the graph to an acyclic DTO instead. `IgnoreCycles` is another option, but it replaces cyclic links with `null` and therefore cannot round-trip the original graph.
