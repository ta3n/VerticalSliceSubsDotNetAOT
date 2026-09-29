# ADR-002: Npgsql + explicit SQL instead of EF Core

Status: Accepted

## Context
Java uses JPA + PostgreSQL with pessimistic row locks. The current .NET app uses EF Core InMemory. Microsoft (EF docs, updated 2026-06) states EF NativeAOT is "highly experimental" and advises against production use; it needs experimental C# interceptors, forbids dynamic queries, and still emits trim/AOT warnings. The measured analyzer run on this repo shows IL2026/IL3050 from `DbContext`.

## Alternatives
| Option | AOT | Verdict |
|---|---|---|
| EF Core + precompiled queries | Experimental | Rejected: contradicts docs' production guidance |
| Dapper / Dapper.AOT | Possible | Extra dependency for ~10 queries |
| Npgsql `NpgsqlDataSource` + hand SQL | Driver-level, no ORM | **Chosen** |

## Decision
`NpgsqlDataSource` registered in DI; small per-aggregate stores (`SubscriptionStore`, `PlanStore`, `InvoiceStore`) holding SQL, explicit column projections, `SELECT … FOR UPDATE` in a transaction for state changes, `CancellationToken` everywhere. Domain rules live in plain domain classes (unit testable without a DB). The abstraction is the store, not a generic repository/unit-of-work.

## Consequences
+ No reflection ORM, SQL is reviewable, identical locking semantics to Java. − No change tracking/migrations generation; SQL is maintained by hand and covered by Testcontainers tests. Reversible: stores can be re-implemented over EF once it is production-supported.
