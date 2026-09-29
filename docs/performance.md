# Performance observations

Measured on 2026-09-30 on a developer workstation (Linux, .NET SDK 10.0.201, Docker 29.4.1, PostgreSQL 17 and Redis 7 in local containers).
Single machine, not isolated: the load generator (`hey`, in a container on the host network) shares CPUs with the server, and other
desktop workloads were running. Treat the numbers as **indicative comparisons, not benchmarks**. Scripts: `scripts/measure-startup.sh`, `scripts/aot-smoke.sh`.

## Results

| Metric | .NET 8 | Original app (net10 JIT, EF InMemory) | New app, net10 JIT | New app, net10 Native AOT |
|---|---:|---:|---:|---:|
| Startup: launch → first HTTP 2xx/4xx (5 runs; original: 3 runs) | Not measured¹ | 568–711 ms | 249–256 ms | 53–55 ms (one cold run 153 ms) |
| RSS at first response | Not measured¹ | 91–92 MB | 72 MB | 35 MB |
| RSS after 14 s of load | Not measured¹ | Not measured² | 108 MB | 51–54 MB |
| Publish output | Not measured¹ | 3.6 MB (needs shared runtime) | 5.9 MB (needs shared runtime) | 27.6 MB single native binary (migrator: 12.1 MB) |
| Container image | Not measured³ | Not measured³ | Not measured⁴ | 61.3 MB (API) / 38.0 MB (migrator) |
| P50 latency, `GET /plans` (c=32, 10 s) | Not measured¹ | Not measured² | 1.1 ms | 1.1 ms |
| P95 latency | Not measured¹ | Not measured² | 2.0 ms | 1.8–1.9 ms |
| P99 latency | Not measured¹ | Not measured² | 2.5–2.6 ms | 2.3–2.4 ms |
| Throughput | Not measured¹ | Not measured² | 26.8–27.1 k req/s | 27.3–27.7 k req/s |

¹ No .NET 8 version of this application exists: the repository was already on `net10.0`, so there is nothing to build for a .NET 8 column.
² The original app has no equivalent workload (`/plans` does not exist there and it has no database), so a latency comparison would not be like-for-like.
³ The original repository has no Dockerfile.
⁴ The JIT image was not built; only the AOT Dockerfile exists.

Load runs: two runs per server, alternating; 4 s warm-up then 10 s measured, 32 connections, server reading PostgreSQL on every request.
One client-side error was reported in one of the four runs (a `hey` "Error distribution" entry); its cause was not investigated (a request cut off
at the `-z` deadline is the likely but unverified explanation). All other runs: 0 errors.

## The original app under Native AOT

For the record, the untouched original code *can* be published with `PublishAot` (23 MB binary, ~59 ms start, 41 MB RSS, with IL2026/IL2104/IL3050/IL3053 warnings
from EF Core, EF InMemory and FluentValidation) but is **not functional**: `POST /subscriptions` returns HTTP 500 because
`JsonTypeInfo metadata for type 'CreateSubscriptionCommand' was not provided by TypeInfoResolver`. Its start-up and size are therefore not a valid comparison point.

## Reading the numbers

* Native AOT's clear wins here are startup (~5× faster than the new JIT build, >10× faster than the original), memory (~2× lower) and image size.
* Steady-state throughput and tail latency are equal within noise: the request path is dominated by PostgreSQL and the loopback network, not by JIT vs AOT code quality.
* The AOT binary is larger on disk than a framework-dependent JIT publish because it contains the runtime and the used framework code; the comparison that matters operationally is the container image (61 MB, no .NET runtime layer).
