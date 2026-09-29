-- Billing-plan catalog: the templates a subscription can be created from.
-- Mirrors the com.example.demo.features.plans.Plan entity mapping.
CREATE TABLE plans (
    code       VARCHAR(64)   NOT NULL,
    name       VARCHAR(200)  NOT NULL,
    amount     NUMERIC(20,4) NOT NULL,
    currency          VARCHAR(3)    NOT NULL,
    billing_interval  VARCHAR(8)    NOT NULL,
    trial_days INTEGER       NOT NULL,
    active     BOOLEAN       NOT NULL,
    CONSTRAINT pk_plans PRIMARY KEY (code),
    CONSTRAINT ck_plans_amount CHECK (amount >= 0),
    CONSTRAINT ck_plans_interval CHECK (billing_interval IN ('MONTH', 'YEAR'))
);

-- Dev-convenience seed catalog.
INSERT INTO plans (code, name, amount, currency, billing_interval, trial_days, active) VALUES
    ('free', 'Free', 0.00, 'USD', 'MONTH', 0, TRUE),
    ('pro-monthly', 'Pro (monthly)', 12.00, 'USD', 'MONTH', 14, TRUE),
    ('pro-yearly', 'Pro (yearly)', 120.00, 'USD', 'YEAR', 14, TRUE);
