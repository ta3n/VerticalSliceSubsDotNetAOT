-- Service-to-service messaging. The API writes a message here in the same transaction as the business change
-- (transactional outbox); the Relay service deletes rows in id order and publishes them to the broker.
-- traceparent is the W3C trace context of the request that wrote the row, so the trace continues across the hop.
CREATE TABLE outbox_messages (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    type        VARCHAR(200) NOT NULL,
    payload     TEXT         NOT NULL,
    traceparent VARCHAR(55)  NULL,
    occurred_at TIMESTAMP    NOT NULL
);

-- The payment that settled the invoice (POST /invoices/{id}/settle); NULL for invoices paid manually or never paid.
ALTER TABLE invoices ADD COLUMN payment_id UUID NULL;
