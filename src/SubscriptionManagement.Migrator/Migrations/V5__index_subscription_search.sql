-- Keyset pagination of POST /subscriptions/search orders by (start_date DESC, id DESC), optionally filtered by status.
-- Without these indexes every page is a sequential scan plus a sort of the whole table.
CREATE INDEX ix_subscriptions_start_date_id ON subscriptions (start_date DESC, id DESC);
CREATE INDEX ix_subscriptions_status_start_date_id ON subscriptions (status, start_date DESC, id DESC);
