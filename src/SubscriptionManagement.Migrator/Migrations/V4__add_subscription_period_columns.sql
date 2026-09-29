-- Internal billing-period tracking added for the renew use case. Not part of the
-- public SubscriptionResponse contract (that stays at its original 6 fields).
ALTER TABLE subscriptions ADD COLUMN current_period_start DATE;
ALTER TABLE subscriptions ADD COLUMN current_period_end DATE;

-- Backfill existing rows: the trial (when present) is the first billing period,
-- otherwise assume monthly billing from start_date. Mirrors the Subscription
-- constructor's defaulting so historical rows behave the same as new ones.
UPDATE subscriptions SET current_period_start = start_date;
UPDATE subscriptions SET current_period_end =
    COALESCE(trial_ends_at, CAST(start_date + INTERVAL '1' MONTH AS DATE));

ALTER TABLE subscriptions ALTER COLUMN current_period_start SET NOT NULL;
ALTER TABLE subscriptions ALTER COLUMN current_period_end SET NOT NULL;
