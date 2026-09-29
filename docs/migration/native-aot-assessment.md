# Native AOT assessment

Status: **initial assessment (pre-implementation)**. The "Result" section at the bottom is updated after the real publish.

## 1. Measured audit of the current code

`dotnet build -c Release -p:PublishAot=true -p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true` on the untouched repo:

| Warning | Location | Cause | Resolution |
|---|---|---|---|
| IL2026 | `Subscription.cs` `[MaxLength(100)]` | DataAnnotations attribute uses reflection | Remove attribute; length enforced by explicit validation and the `VARCHAR(100)` column |
| IL2026 + IL3050 | `AppDbContext` ctor | EF Core is "not fully compatible with trimming/NativeAOT" | Remove EF Core from the runtime path (ADR-002) |

Manual search: no `Assembly.Load`, `Activator`, `dynamic`, `Reflection.Emit`, or plugin loading in the repo.
Indirect reflection: `AddValidatorsFromAssemblyContaining` (assembly scan), FluentValidation expression trees
(`RuleFor(x => …)` compiled at runtime), and reflection-based `System.Text.Json` for `Results.Ok(subscription)`.

## 2. Official position (consulted 2026-09-30)

| Topic | Statement | Source |
|---|---|---|
| EF Core + NativeAOT | "highly experimental … not yet suited for production use … recommend against deploying EF NativeAOT applications in production"; requires precompiled queries via experimental C# interceptors; dynamic queries unsupported; publish "currently reports a number of trimming and NativeAOT warnings"; providers must add support | https://learn.microsoft.com/ef/core/performance/nativeaot-and-precompiled-queries |
| ASP.NET Core feature matrix | Minimal APIs partial; MVC, OData, Session, Blazor Server, "other authentication" unsupported; HealthChecks, JWT, OutputCaching, RateLimiting, gRPC supported | https://learn.microsoft.com/aspnet/core/fundamentals/native-aot |
| Minimal API JSON | Every type in a request/response body must be on a `JsonSerializerContext` registered in `ConfigureHttpJsonOptions`; use `CreateSlimBuilder` | same |
| Npgsql | The Npgsql docs landing page states nothing about AOT; the ADO.NET driver's AOT status must be verified empirically (done by publish + smoke test below) | https://www.npgsql.org/doc/index.html |
| AOT warnings policy | An app that emits AOT warnings at publish "might not work correctly" | ASP.NET Core Native AOT page |
| Other references | https://learn.microsoft.com/dotnet/core/deploying/native-aot · /dotnet/core/deploying/trimming/fixing-warnings · /dotnet/standard/serialization/system-text-json/source-generation |

## 3. Decision summary

* **EF Core is not used at runtime.** Microsoft explicitly advises against EF NativeAOT in production. Adopting it would mean an
  experimental compiler feature (interceptors) plus unresolved warnings, violating "no unexplained AOT warnings" and "secure/maintainable".
  Replacement: Npgsql + explicit SQL with explicit projections (ADR-002).
* **Migrations are a separate executable** running versioned SQL (ADR-003), so schema tooling never needs to run inside the AOT app.
* **FluentValidation is removed**; validators are plain C# (ADR-004).
* **Minimal APIs with `CreateSlimBuilder`** and a single `AppJsonSerializerContext` (already the model; no MVC rewrite needed) (ADR-001).
* Kestrel HTTP only (TLS terminated at the ingress, per the Microsoft template).

## 4. Risk items to verify at publish time

| Item | Expectation | Verification |
|---|---|---|
| Npgsql `NpgsqlDataSource` under AOT | Works; may warn on enum/type-mapping reflection paths | Publish + integration smoke |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | May warn | Publish warnings |
| OpenTelemetry (OTLP exporter, ASP.NET Core instrumentation) | Should be AOT-annotated | Publish warnings |
| `Microsoft.AspNetCore.OpenApi` / Scalar | OpenApi supported; Scalar unknown | Publish warnings; drop Scalar if it fails |
| Unhandled-exception handler + `ProblemDetails` | Needs source-generated context | Smoke test |

Any warning found is recorded below with root cause and disposition; suppressions are not used unless individually justified here.

## 5. Result

_Filled in after the publish (see bottom of the migration report)._
