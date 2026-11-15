# ADR-005: JIT messaging services beside the Native AOT API

Status: Accepted

## Context
The API's Native AOT constraints still apply: explicit SQL, source-generated JSON and no runtime handler discovery.
Wolverine, EF Core and Marten use runtime code generation. The extension needs gRPC quotes, a durable renewal saga,
message-driven compensation and payment history without changing the existing synchronous renewal/payment endpoints.

## Alternatives
| Option | Verdict |
|---|---|
| Add Wolverine/EF Core directly to the API | Rejected: breaks the API's AOT boundary |
| Replace the API with a JIT service | Rejected: gives up an existing requirement and changes the deployment baseline |
| Implement a broker runtime and saga persistence by hand | Rejected: duplicates durable delivery, concurrency and recovery machinery |
| Separate JIT services with generated contracts | **Chosen** |

## Decision
Keep `SubscriptionManagement` Native AOT. Add three JIT executables: `billing` (gRPC, EF Core renewal saga and credit
notes), `payments` (Marten payment streams) and `relay` (outbox draining and HTTP commands). Use Wolverine 6.64.0 with
RabbitMQ conventional routing and durable inbox/outbox policies. Billing also makes its local delayed timeout queue durable.
Billing and Payments explicitly declare/bind queues for outgoing command/reply types hidden in object collections,
so those messages have a destination even before their consumers first start.

`SubscriptionManagement.Contracts` contains plain message records and `billing.proto`, without runtime package
dependencies. The API compiles the proto as a client and Billing as a server. Billing separates cleartext HTTP/1.1
REST/probes on port 5128 from HTTP/2 gRPC on 5138. API gRPC calls have a two-second deadline. New API behaviour is
opt-in through `Billing:GrpcAddress` and `Outbox:Enabled`; Aspire supplies both and starts all dependencies.

Billing orchestrates collection and retains terminal saga rows for status queries. Cancellation/refund is a choreography:
Payments reacts to a subscription cancellation and Billing reacts to its refund. Services share telemetry defaults,
but each owns its state and transactions; no transaction spans services.

## Consequences
The API retains its AOT contract and existing endpoints. Runtime-generated infrastructure is isolated and each service
can be tested through its own host. Delivery is at least once, so handlers must keep their idempotency and optimistic
concurrency checks. The local stack needs RabbitMQ, three more processes and separate Billing/Payments databases.
A timeout only acts before payment confirmation; `Settling` can require operator recovery after Relay exhausts retries.
Aspire wires this topology; compose, Helm/Kubernetes and CI images for the new services are not yet supplied.
