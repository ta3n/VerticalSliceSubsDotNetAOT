# ADR-006: Raw SQL outbox and a JIT Relay

Status: Accepted

## Context
An API request must commit its invoice/cancellation and its event together. Publishing to RabbitMQ after a database
commit could lose the event on a crash; publishing before commit could expose a business change that later rolls back.
The AOT API cannot take Wolverine/EF Core runtime dependencies. The API must also remain the only writer of its
subscriptions/invoices, preserving its row locks, validation and cache behaviour.

## Alternatives
| Option | Verdict |
|---|---|
| Publish directly before/after the business commit | Rejected: creates an unprotected database/broker failure window |
| Add Wolverine's EF integration to the API | Rejected: violates the Native AOT boundary |
| Let Billing/Payments write the API tables | Rejected: bypasses the API's domain and cache rules |
| Raw SQL outbox with a JIT adapter | **Chosen** |

## Decision
Migration V6 adds `outbox_messages` and nullable `invoices.payment_id`. With `Outbox:Enabled`, the API inserts
`InvoiceIssued` or an eligible `SubscriptionCanceled` using the same Npgsql connection/transaction as the business
write. Payload JSON is source generated; each row stores the current W3C `traceparent`.
Settlement locks the subscription before its invoice, serializing with cancellation. If an open invoice is first
settled after its subscription was canceled, that settlement transaction also writes `SubscriptionCanceled`; the
late capture therefore still starts the refund flow. Repeated settlement does not emit another cancellation fact.

Relay connects to the same `subscriptions` database but owns only its `relay` durable-envelope schema. Its drain
claims known types in id order with `FOR UPDATE SKIP LOCKED`, deletes them and inserts Wolverine outgoing envelopes
in one transaction through `IDbContextOutbox<RelayDbContext>`. Wolverine then delivers the envelopes to RabbitMQ.
A rollback preserves the raw rows. Unknown types stay untouched and generate a rate-limited warning; an unreadable
known payload rolls back the batch. Each row restores its own parent trace and correlation id.

Relay also consumes `SettleInvoice` and `CancelRenewal`. It calls the API over HTTP for settlement, void and pause;
200/404/409 become domain outcomes, while transient failures and HTTP timeouts retry. Settlement replies drive the
Billing saga. Repeated settlement with the same payment id and repeated voids are no-ops; cancel-renewal pauses only
after a successful void, so an already-paid invoice does not pause an active subscription.

## Consequences
The handoff is atomic without placing a message framework in the API. Delivery remains at least once, not exactly
once; business idempotency handles redelivery. Relay must share the API database to make the raw-row/envelope transfer
atomic, and needs coordinated schema permissions. Corrupt known messages can hold back their batch until repaired;
unknown types need a compatible Relay release. There is no operator replay UI in this sample. The legacy `/renew`
and manual `/pay` paths do not emit collected-payment events and retain their previous behaviour.
