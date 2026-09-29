-- Billing documents issued when a subscription starts or renews a paid period.
-- Mirrors the com.example.demo.features.invoices.Invoice entity mapping.
CREATE TABLE invoices (
    id              UUID          NOT NULL,
    subscription_id UUID          NOT NULL,
    amount          NUMERIC(20,4) NOT NULL,
    currency        VARCHAR(3)    NOT NULL,
    period_start    DATE          NOT NULL,
    period_end      DATE          NOT NULL,
    status          VARCHAR(8)    NOT NULL,
    issued_at       TIMESTAMP     NOT NULL,
    paid_at         TIMESTAMP,
    CONSTRAINT pk_invoices PRIMARY KEY (id),
    CONSTRAINT ck_invoices_amount CHECK (amount >= 0),
    CONSTRAINT ck_invoices_status CHECK (status IN ('OPEN', 'PAID', 'VOID'))
);

CREATE INDEX ix_invoices_subscription_id ON invoices (subscription_id);
