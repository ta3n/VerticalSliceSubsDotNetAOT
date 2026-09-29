# .NET 8 → .NET 10 compatibility report

## Starting point (verified)

`SubscriptionManagement.csproj` already has `<TargetFramework>net10.0</TargetFramework>`; SDK 10.0.201 is installed (also 8.0.419, 9.0.312).
There is no `global.json`. Therefore **no .NET 8 → 10 upgrade is required**; this report audits the existing packages
against .NET 10 and Native AOT and pins the SDK.

## Package audit

| Package | Version | .NET 10 | AOT / trimming | Maintained | Disposition |
|---|---|---|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.3 | ✔ | ✘ experimental (IL2026/IL3050 observed) | ✔ | **Removed** (ADR-002) |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.3 | ✔ | ✘; also not a valid proof of relational behaviour | ✔ | **Removed** |
| FluentValidation (+DI extensions) | 12.1.1 | ✔ | Assembly scanning + runtime-compiled expressions; not annotated AOT-safe | ✔ | **Removed** (ADR-004) |

New packages are added per slice and each is checked by publishing (see `native-aot-assessment.md`): Npgsql, Microsoft.Extensions.Caching.StackExchangeRedis,
Microsoft.Extensions.Diagnostics.HealthChecks (built-in), Microsoft.AspNetCore.OpenApi, OpenTelemetry.* (versions resolved from NuGet at implementation time).
Test-only packages (xUnit, Testcontainers.PostgreSql, Testcontainers.Redis, Microsoft.AspNetCore.Mvc.Testing) do not ship in the AOT binary.

## Breaking-change review relevant to this code

Reviewed for the code that exists (3 endpoints, one DbContext, one validator). None of the following affect it as written; each is a check, not an assumption:

| Area | Relevance |
|---|---|
| ASP.NET Core minimal API validation (.NET 10 built-in `AddValidation`) | Uses source generation, could replace hand validation later; not adopted now because the cross-field rule and problem shape are custom |
| `IExceptionHandler` / `ProblemDetails` | Used for the error pipeline; needs source-gen contexts |
| Json: default `JsonSerializerOptions` web defaults | `ConfigureHttpJsonOptions` used; camelCase kept; enums as numbers (compat) |
| EF 10 breaking changes | Moot after EF removal |
| Container base images | Runtime-deps image `mcr.microsoft.com/dotnet/runtime-deps:10.0` for AOT |

## SDK/build changes

* Add `global.json` pinning SDK `10.0.x` with `rollForward: latestFeature`.
* `Directory.Build.props`: `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` for IL* trim/AOT warnings (`<WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2057;IL2067;IL2070;IL3050</WarningsAsErrors>`), `InvariantGlobalization`.
* No blanket `NoWarn`.
