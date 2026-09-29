# ADR-003: Versioned SQL migrations run by a dedicated executable

Status: Accepted

## Context
Java uses Flyway (V1–V4). `dotnet ef` migrations require the EF tooling model and are unsuitable inside an AOT binary.

## Decision
`SubscriptionManagement.Migrator` is a separate console project (also AOT-publishable) embedding `V1…V4` SQL files (same schema as Java). It records applied versions and checksums in `schema_migrations`, takes a `pg_advisory_lock` to be safe under concurrent starts, and fails if an applied migration's checksum changed. The API never migrates on startup; compose/Kubernetes run the migrator as a one-shot job before the app. Tests call the same runner against a Testcontainers database.

## Consequences
Explicit operational step; no race between app replicas. Uses its own history table, so it must not share a database with Flyway's `flyway_schema_history` from the Java app.
