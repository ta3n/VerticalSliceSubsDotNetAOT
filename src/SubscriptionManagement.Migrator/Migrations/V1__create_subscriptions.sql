-- Baseline schema for the subscriptions vertical slice.
-- Mirrors the com.example.demo.features.subscriptions.Subscription entity mapping.
CREATE TABLE subscriptions (
    id            UUID            NOT NULL,
    service_name  VARCHAR(100)    NOT NULL,
    price         NUMERIC(57, 28) NOT NULL,
    start_date    DATE            NOT NULL,
    trial_ends_at DATE,
    status        VARCHAR(16)     NOT NULL,
    CONSTRAINT pk_subscriptions PRIMARY KEY (id)
);
