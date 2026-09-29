# ADR-001: Native AOT with Minimal APIs and source-generated JSON

Status: Accepted

## Context
The app is already Minimal APIs on .NET 10. Microsoft's Native AOT matrix marks Minimal APIs "partial" (JSON must be source generated), MVC unsupported.

## Decision
Keep Minimal APIs (no rewrite needed). Use `WebApplication.CreateSlimBuilder`, HTTP-only Kestrel, one `AppJsonSerializerContext` registered via `ConfigureHttpJsonOptions`, `PublishAot=true`, and analyzers with IL2026/IL2057/IL2067/IL2070/IL3050 as errors. No global warning suppression.

## Consequences
+ Fast start, small footprint. − Every wire type must be listed on the context; libraries must be AOT-clean or replaced. Local `dotnet run` still uses JIT, so CI must publish and smoke test the native binary.
