# ADR-007: Event-sourced payments with Marten

Status: Accepted

## Context
The extension needs to distinguish requested, captured, declined, canceled and refunded payments and explain the
history behind a current status. A cancellation can overtake its charge; duplicate messages and concurrent refund
requests must not collect/refund twice. The sample gateway is deterministic by amount rather than a real provider.

## Alternatives
| Option | Verdict |
|---|---|
| Mutable payment row only | Rejected: loses the transition history required by this extension |
| Hand-maintained event table plus separate mutable projection | Rejected: adds projection and concurrency machinery already available in Marten |
| Event-source subscriptions and invoices too | Rejected: expands scope and replaces the API's established SQL model |
| Marten stream plus inline snapshot, integrated with Wolverine | **Chosen** |

## Decision
Use Marten 9.59.0 in the JIT Payments service. Stream id = payment id; events, `Payment` snapshots and Wolverine
envelopes live in the `payments` schema of the `payments` database. `FetchForWriting<Payment>` protects stream writes
with optimistic concurrency; handlers append a pure decider's events and return messages. Wolverine/Marten commits
the events, inline snapshot and outgoing envelopes in one transaction. No asynchronous projection daemon is needed.

Events are `ChargeRequested`, `ChargeCaptured`, `ChargeDeclined`, `ChargeCanceled`, `ChargeRefunded`. An existing stream
makes a repeated charge a no-op. Cancel before charge can create a one-event `ChargeCanceled` stream, preventing later
collection. Cancel after capture appends `ChargeRefunded` and publishes `PaymentRefunded`; declined/canceled/refunded
payments do not change again. A subscription cancellation refunds captured payments only when their billed period
ends after the fact's `CanceledOn` day. When cancellation wins a race with settlement, the API emits the cancellation
fact from the first settlement transaction too, so a late capture is still refunded when eligible. Billing makes the
corresponding credit note idempotent through a unique payment-id constraint.

`GET /payments/{id}` exposes the inline snapshot; `/payments/{id}/events` returns the ordered stream. The simulated
gateway checks `Gateway:NoReplyAtOrAbove` before `Gateway:DeclineAtOrAbove`; its decision uses the taxed invoice total.
The no-reply branch records `ChargeRequested` and lets Billing's durable timeout initiate compensation.

## Consequences
An ordered history explains the current payment and supports rebuilding its snapshot. Inline projection avoids
read lag after a committed handler but adds work to each event transaction. Stored event shapes become a compatibility
contract; future changes need an event-versioning/upcasting strategy. Stream-version and initial-stream collisions
are retried, then decisions run against current state. The guarantee covers simulated state only: integrating a real
payment provider will also require provider-side idempotency, reconciliation and secure credential handling.
