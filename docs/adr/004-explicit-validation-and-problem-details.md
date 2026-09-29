# ADR-004: Hand-written validation and RFC 9457 error pipeline

Status: Accepted

## Context
FluentValidation scans assemblies and compiles expression trees at runtime; it is not annotated AOT-safe. Java returns RFC 9457 problem+json for every error with `type`, `instance`, `timestamp`, `traceId`, and an `errors` map.

## Decision
Validators are small static methods returning `Dictionary<string,string[]>` (camelCase field names); domain failures throw `ApiProblemException` (status, type slug, title, detail, extensions); a single `IExceptionHandler` + problem writer adds `instance`, `timestamp`, `traceId` and serialises through the source-generated context. Unexpected exceptions become an opaque 500 and are logged with the trace id. A correlation middleware honours/echoes `X-Trace-Id`.

## Consequences
Small amount of hand-written code, no runtime discovery, identical wire shape to the Java reference. Documented key-casing change for validation errors (see java-feature-gap §4).
