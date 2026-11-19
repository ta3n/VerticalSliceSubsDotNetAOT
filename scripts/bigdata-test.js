// Big-data run: grows the system to a large dataset in phases and shows what that does to it (Postgres / PgBouncer,
// the RabbitMQ saga, the Kafka stream into Reporting). Run it from the Aspire dashboard (▶ on k6-bigdata) and watch the
// Grafana "Big data" dashboard; the phases are tagged `scenario` / `phase`, so every graph can be cut per phase.
//
//   t0            seed     BIGDATA_SUBSCRIPTIONS subscriptions at BIGDATA_SEED_RATE/s, start dates spread over the last year,
//                          prices chosen so the renewals later end as a mix of captured / declined / no-reply payments
//   then          renew    BIGDATA_RENEWAL_PERCENT % of them renewed at BIGDATA_RENEW_RATE/s, each on a random existing
//                          subscription (a random keyset cursor = random access into the big table): invoice → outbox →
//                          Relay → RabbitMQ saga → Payments → Kafka → Reporting
//   then, together cancel  BIGDATA_CANCEL_PERCENT % canceled at BIGDATA_CANCEL_RATE/s (captured payments are refunded)
//                  read    BIGDATA_READ_VUS VUs for BIGDATA_READ_DURATION: random pages, get by id, invoices, plans, Reporting
//                  monitor (only with REPORTING_URL) polls Reporting during the whole run: how far it lags behind the
//                          renewals, and how long it takes to catch up once the last renewal is accepted
//
// Sizes: BIGDATA_PROFILE=small (2k subscriptions, smoke) | medium (50k, default) | large (250k); every BIGDATA_* knob
// below overrides the profile. Rates are arrival rates (requests started per second, regardless of how slow the system
// answers), so a phase lasts count / rate and `dropped_iterations` shows when the load generator itself saturates.
//   BIGDATA_SUBSCRIPTIONS · BIGDATA_RENEWAL_PERCENT (20) · BIGDATA_CANCEL_PERCENT (5)
//   BIGDATA_SEED_RATE · BIGDATA_RENEW_RATE · BIGDATA_CANCEL_RATE · BIGDATA_READ_VUS · BIGDATA_READ_DURATION
//   BIGDATA_DECLINE_PERCENT (12) · BIGDATA_NOREPLY_PERCENT (2)   share of seeded prices that end declined / unanswered
//   BIGDATA_DECLINE_AT (5000) · BIGDATA_NOREPLY_AT (9000)        Payments' Gateway:DeclineAtOrAbove / NoReplyAtOrAbove
//   BIGDATA_PLANS (20) plans created in setup() · BIGDATA_MAX_VUS (1000) VU ceiling of an arrival-rate phase
//   BIGDATA_SETTLE_S (180) how long after the last renewal Reporting may take to catch up · BIGDATA_TOLERANCE (0.05, share of the scheduled renewals k6 may drop) · BIGDATA_QUIET_S (10)
//   REPORTING_URL (unset: no monitor) · BASE_URL (http://localhost:8080) · HTTP_TIMEOUT (10s) · KEYCLOAK_* (lib/api.js)
//
// Custom metrics (Prometheus: k6_<name>): bigdata_subscriptions_created, bigdata_renewals_accepted,
// bigdata_cancellations_accepted (counters) · bigdata_reporting_invoices, bigdata_reporting_expected_invoices,
// bigdata_reporting_invoice_lag, bigdata_reporting_payments_settled, bigdata_reporting_canceled (gauges, since the run
// began) · bigdata_time_to_consistency_seconds, bigdata_search_random_page_duration (trends).
// "Expected invoices" is the schedule (renew rate × elapsed time), not a count: if the generator drops iterations the
// lag is overstated; read it next to dropped_iterations.
// Fails when a phase's error rate or p95 is out of bounds, when the generator drops more than 1% of a phase, or when
// Reporting does not reflect the renewals within BIGDATA_SETTLE_S.
import http from 'k6/http';
import encoding from 'k6/encoding';
import exec from 'k6/execution';
import {check, sleep} from 'k6';
import {Counter, Gauge, Rate, Trend} from 'k6/metrics';
import {
    api,
    env,
    envNumber,
    expectStatus,
    params,
    pick,
    planBody,
    seconds,
    subscriptionBody,
    unique
} from './lib/api.js';

// ---------------------------------------------------------------- sizing
const PROFILES = {
    small: {subscriptions: 2000, seedRate: 100, renewRate: 40, cancelRate: 20, readVus: 10, readDuration: '30s'},
    medium: {subscriptions: 50000, seedRate: 400, renewRate: 150, cancelRate: 60, readVus: 50, readDuration: '3m'},
    large: {subscriptions: 250000, seedRate: 800, renewRate: 300, cancelRate: 120, readVus: 100, readDuration: '5m'},
};
const profileName = env('BIGDATA_PROFILE', 'medium');
const profile = PROFILES[profileName];
if (!profile) throw new Error(`BIGDATA_PROFILE must be one of ${Object.keys(PROFILES).join(', ')}`);

const subscriptions = envNumber('BIGDATA_SUBSCRIPTIONS', profile.subscriptions);
const renewalPercent = envNumber('BIGDATA_RENEWAL_PERCENT', 20);
const cancelPercent = envNumber('BIGDATA_CANCEL_PERCENT', 5);
const seedRate = envNumber('BIGDATA_SEED_RATE', profile.seedRate);
const renewRate = envNumber('BIGDATA_RENEW_RATE', profile.renewRate);
const cancelRate = envNumber('BIGDATA_CANCEL_RATE', profile.cancelRate);
const readVus = envNumber('BIGDATA_READ_VUS', profile.readVus);
const readDuration = env('BIGDATA_READ_DURATION', profile.readDuration);
const declinePercent = envNumber('BIGDATA_DECLINE_PERCENT', 12);
const noReplyPercent = envNumber('BIGDATA_NOREPLY_PERCENT', 2);
const declineAt = envNumber('BIGDATA_DECLINE_AT', 5000);
const noReplyAt = envNumber('BIGDATA_NOREPLY_AT', 9000);
const planCount = envNumber('BIGDATA_PLANS', 20);
const maxVus = envNumber('BIGDATA_MAX_VUS', 1000);
const settleSeconds = envNumber('BIGDATA_SETTLE_S', 180);
const tolerance = envNumber('BIGDATA_TOLERANCE', 0.05);
const quietSeconds = envNumber('BIGDATA_QUIET_S', 10);
const reportingUrl = env('REPORTING_URL', '').replace(/\/+$/, '');

const positive = {subscriptions, seedRate, renewRate, cancelRate, readVus, settleSeconds, maxVus};
for (const [name, value] of Object.entries(positive)) {
    if (!Number.isFinite(value) || value <= 0) throw new Error(`BIGDATA ${name} must be positive`);
}
for (const [name, value] of Object.entries({renewalPercent, cancelPercent, declinePercent, noReplyPercent})) {
    if (!Number.isFinite(value) || value < 0 || value > 100) throw new Error(`BIGDATA ${name} must be between 0 and 100`);
}
if (declinePercent + noReplyPercent > 100) throw new Error('BIGDATA_DECLINE_PERCENT + BIGDATA_NOREPLY_PERCENT must not exceed 100');
if (!(declineAt > 1 && noReplyAt > declineAt)) throw new Error('BIGDATA_NOREPLY_AT must be above BIGDATA_DECLINE_AT, which must be above 1');

const renewals = Math.round(subscriptions * renewalPercent / 100);
const cancellations = Math.round(subscriptions * cancelPercent / 100);
const phaseSeconds = (count, rate) => Math.max(1, Math.ceil(count / rate));
const seedSeconds = phaseSeconds(subscriptions, seedRate);
const renewSeconds = renewals > 0 ? phaseSeconds(renewals, renewRate) : 0;
const cancelSeconds = cancellations > 0 ? phaseSeconds(cancellations, cancelRate) : 0;
const readSeconds = seconds(readDuration);

const GAP = 5; // seconds between phases, so Grafana shows them apart
const renewStart = seedSeconds + GAP;
const renewEnd = renewStart + renewSeconds;
const finalStart = renewEnd + GAP; // cancel, read and the monitor's catch-up window run together from here
const monitorSeconds = finalStart + Math.max(settleSeconds, readSeconds, cancelSeconds);

// ---------------------------------------------------------------- metrics
const created = new Counter('bigdata_subscriptions_created');
const renewalsAccepted = new Counter('bigdata_renewals_accepted');
const renewalMisses = new Counter('bigdata_renewal_retries');
const cancelsAccepted = new Counter('bigdata_cancellations_accepted');
const cancelMisses = new Counter('bigdata_cancel_retries');
const randomPage = new Trend('bigdata_search_random_page_duration', true);
const reportingInvoices = new Gauge('bigdata_reporting_invoices');
const reportingExpected = new Gauge('bigdata_reporting_expected_invoices');
const reportingLag = new Gauge('bigdata_reporting_invoice_lag');
const reportingPayments = new Gauge('bigdata_reporting_payments_settled');
const reportingCanceled = new Gauge('bigdata_reporting_canceled');
const timeToConsistency = new Trend('bigdata_time_to_consistency_seconds');
const reportingConsistent = new Rate('bigdata_reporting_consistent');

// ---------------------------------------------------------------- scenarios
function arrival(exec_, rate, durationSeconds, startSeconds, phase) {
    return {
        executor: 'constant-arrival-rate',
        exec: exec_,
        rate,
        timeUnit: '1s',
        duration: `${durationSeconds}s`,
        startTime: `${startSeconds}s`,
        // Plenty up front: a VU the phase has to start on demand is slow, and the iterations that wait for it are dropped.
        preAllocatedVUs: Math.min(maxVus, Math.max(50, Math.ceil(rate / 2))),
        maxVUs: maxVus,
        tags: {phase},
    };
}

const scenarios = {seed: arrival('seed', seedRate, seedSeconds, 0, 'seed')};
if (renewals > 0) scenarios.renew = arrival('renew', renewRate, renewSeconds, renewStart, 'renew');
if (cancellations > 0) scenarios.cancel = arrival('cancel', cancelRate, cancelSeconds, finalStart, 'cancel');
scenarios.read = {
    executor: 'constant-vus',
    exec: 'read',
    vus: readVus,
    duration: readDuration,
    startTime: `${finalStart}s`,
    tags: {phase: 'read'},
};
if (reportingUrl) {
    // One long iteration that polls until Reporting has caught up and the other phases are over (or the window ends).
    scenarios.monitor = {
        executor: 'per-vu-iterations',
        exec: 'monitor',
        vus: 1,
        iterations: 1,
        maxDuration: `${monitorSeconds + 15}s`,
        tags: {phase: 'monitor'},
    };
}

// Dropped iterations beyond 1% of a phase mean the load generator, not the system, was the limit.
const droppedAllowed = (count) => `count<=${Math.ceil(count * 0.01)}`;
const thresholds = {
    checks: ['rate>0.99'],
    'http_req_failed{scenario:seed}': ['rate<0.01'],
    'http_req_duration{scenario:seed}': ['p(95)<800'],
    'http_req_failed{scenario:read}': ['rate<0.01'],
    'http_req_duration{scenario:read}': ['p(95)<500'],
    bigdata_search_random_page_duration: ['p(95)<500'],
    'dropped_iterations{scenario:seed}': [droppedAllowed(subscriptions)],
};
if (renewals > 0) {
    thresholds['http_req_failed{scenario:renew}'] = ['rate<0.05']; // search misses and already-open renewals are retried, not failures
    thresholds['http_req_duration{scenario:renew}'] = ['p(95)<1500'];
    thresholds['dropped_iterations{scenario:renew}'] = [droppedAllowed(renewals)];
}
if (cancellations > 0) {
    thresholds['http_req_failed{scenario:cancel}'] = ['rate<0.05'];
    thresholds['http_req_duration{scenario:cancel}'] = ['p(95)<1500'];
    thresholds['dropped_iterations{scenario:cancel}'] = [droppedAllowed(cancellations)];
}
if (reportingUrl && renewals > 0) thresholds.bigdata_reporting_consistent = ['rate==1'];

export const options = {
    scenarios,
    thresholds,
    setupTimeout: '120s',
    summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

// ---------------------------------------------------------------- data helpers
const DATE_SPREAD_DAYS = 364;
const DAY_MS = 86400000;
const dateDaysAgo = (days) => new Date(Date.now() - days * DAY_MS).toISOString().slice(0, 10);
const randomInt = (min, max) => min + Math.floor(Math.random() * (max - min + 1));
const hex = (length) => Array.from({length}, () => Math.floor(Math.random() * 16).toString(16)).join('');

/** A price that makes the renewal end as the gateway decides: captured, declined or never answered. */
function seedPrice() {
    const roll = Math.random() * 100;
    if (roll < noReplyPercent) return randomInt(noReplyAt, noReplyAt + 999);
    if (roll < noReplyPercent + declinePercent) return randomInt(declineAt, noReplyAt - 1);
    return randomInt(1, declineAt - 1);
}

/**
 * A keyset cursor at a random point of the (start_date DESC, id DESC) order: the page that follows it is a random place
 * in the big table. Cursors are base64url("yyyy-MM-dd|id"), so one can be made up.
 */
function randomCursor() {
    const id = `${hex(8)}-${hex(4)}-${hex(4)}-${hex(4)}-${hex(12)}`;
    return encoding.b64encode(`${dateDaysAgo(randomInt(0, DATE_SPREAD_DAYS))}|${id}`, 'rawurl');
}

/** Up to 100 subscriptions at a random place of the table (empty when the cursor lands past the last row). */
function randomItems(status) {
    const started = Date.now();
    const page = api.searchSubscriptions({status, limit: 100, cursor: randomCursor()});
    randomPage.add(Date.now() - started);
    check(page, {'random page 200': (r) => r.status === 200});
    return page.status === 200 ? page.json('items') || [] : [];
}

function shuffled(items) {
    const copy = items.slice();
    for (let i = copy.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy;
}

// ---------------------------------------------------------------- reporting
function readReporting() {
    const res = http.get(`${reportingUrl}/reporting/revenue`, params('GET /reporting/revenue'));
    if (res.status !== 200) return null;
    const body = res.json();
    return {
        invoices: (body.currencies || []).reduce((sum, c) => sum + c.invoicesIssued, 0),
        payments: body.paymentsSucceeded + body.paymentsFailed,
        canceled: body.subscriptionsCanceled,
    };
}

// ---------------------------------------------------------------- lifecycle
export function setup() {
    // Plans are part of the data: a catalogue that is read on every renewal screen.
    const run = Date.now().toString(36);
    for (let i = 0; i < planCount; i++) {
        const res = api.createPlan(planBody(`bd-${run}-${i}`, {amount: 5 + i * 5}), [201, 409]);
        check(res, {'setup: plan created': (r) => r.status === 201 || r.status === 409});
    }
    return {baseline: reportingUrl ? readReporting() : null};
}

export function seed() {
    const body = subscriptionBody(unique('Big'), {
        price: seedPrice(),
        startDate: dateDaysAgo(randomInt(0, DATE_SPREAD_DAYS)),
        trialEndsAt: null,
    });
    if (expectStatus(api.createSubscription(body), 201, 'seed: create 201')) created.add(1);
}

// An expected-status list replaces the default 2xx, so it names the success status too (202 / 204).
export function renew() {
    // A random page, then its subscriptions in random order: the first one that accepts a renewal wins.
    for (let attempt = 0; attempt < 3; attempt++) {
        for (const sub of shuffled(randomItems('ACTIVE')).slice(0, 5)) {
            const res = api.startRenewal(sub.id, [202, 400, 404, 409, 422]);
            if (res.status === 202) {
                renewalsAccepted.add(1);
                check(res, {'renew: invoice issued': (r) => Boolean(r.json('invoice.id'))});
                return;
            }
            renewalMisses.add(1);
        }
    }
}

export function cancel() {
    for (let attempt = 0; attempt < 3; attempt++) {
        for (const sub of shuffled(randomItems('ACTIVE')).slice(0, 5)) {
            const res = api.cancelSubscription(sub.id, [204, 400, 404, 409]);
            if (res.status === 204) {
                cancelsAccepted.add(1);
                return;
            }
            cancelMisses.add(1);
        }
    }
}

// Ids seen on recent pages, per VU: what the by-id reads pick from.
let pool = [];

export function read() {
    const roll = Math.random() * 100;
    if (roll < 35 || pool.length === 0) {
        const items = randomItems(Math.random() < 0.5 ? 'ACTIVE' : null);
        if (items.length > 0) pool = items.map((s) => s.id);
        if (items.length > 0 && Math.random() < 0.3) {
            const next = api.searchSubscriptions({limit: 50, cursor: randomCursor()});
            expectStatus(next, 200, 'read: page 200');
        }
    } else if (roll < 65) {
        expectStatus(api.getSubscription(pick(pool)), 200, 'read: get 200');
    } else if (roll < 78) {
        expectStatus(api.listInvoices(pick(pool)), 200, 'read: invoices 200');
    } else if (roll < 88) {
        expectStatus(api.listPlans(), 200, 'read: plans 200');
    } else if (roll < 93) {
        expectStatus(api.getPlan('pro-monthly'), 200, 'read: plan 200');
    } else if (reportingUrl) {
        expectStatus(http.get(`${reportingUrl}/reporting/revenue`, params('GET /reporting/revenue')), 200, 'read: reporting 200');
    } else {
        expectStatus(api.searchSubscriptions({status: 'ACTIVE', limit: 20}), 200, 'read: search 200');
    }
}

export function monitor(data) {
    let caughtUp = false;
    let lastDelta = -1;
    let lastGrowth = 0;
    const phasesEnd = finalStart + Math.max(readSeconds, cancelSeconds);
    for (; ;) {
        const elapsed = exec.instance.currentTestRunDuration / 1000;
        const polled = Date.now();
        const now = readReporting();
        if (now && data.baseline) {
            const delta = now.invoices - data.baseline.invoices;
            const expected = Math.min(renewals, Math.max(0, elapsed - renewStart) * renewRate);
            reportingInvoices.add(delta);
            reportingExpected.add(expected);
            reportingLag.add(expected - delta);
            reportingPayments.add(now.payments - data.baseline.payments);
            reportingCanceled.add(now.canceled - data.baseline.canceled);

            if (delta !== lastDelta) {
                lastDelta = delta;
                lastGrowth = elapsed;
            }
            // Caught up: the renewals are over, nearly all of what they scheduled is in the read model (k6 may drop a few
            // iterations, see BIGDATA_TOLERANCE) and it has stopped growing, so nothing is still on its way through Kafka.
            if (!caughtUp && renewals > 0 && elapsed >= renewEnd && delta >= renewals * (1 - tolerance)
                && elapsed - lastGrowth >= quietSeconds) {
                caughtUp = true;
                timeToConsistency.add(Math.max(0, lastGrowth - renewEnd));
                reportingConsistent.add(true);
            }
        }
        // Done: caught up (or nothing to catch up) and the cancel / read phases are over, or the window is out.
        if ((caughtUp || renewals === 0) && elapsed >= phasesEnd) return;
        if (elapsed >= monitorSeconds) break;
        sleep(Math.max(0, 2 - (Date.now() - polled) / 1000));
    }
    if (!caughtUp && renewals > 0) reportingConsistent.add(false); // never caught up within BIGDATA_SETTLE_S
}

// ---------------------------------------------------------------- summary
const fmt = (value, digits = 0) => (value === undefined || value === null ? '-' : Number(value).toFixed(digits));
const metric = (data, name) => (data.metrics[name] ? data.metrics[name].values : {count: 0});
const pad = (text, width) => String(text).padEnd(width);

export function handleSummary(data) {
    const rows = [];
    const phase = (name, label, planned) => {
        const duration = metric(data, `http_req_duration{scenario:${name}}`);
        const failed = metric(data, `http_req_failed{scenario:${name}}`);
        const dropped = metric(data, `dropped_iterations{scenario:${name}}`);
        if (!data.metrics[`http_req_duration{scenario:${name}}`]) return;
        rows.push(
            `${pad(label, 8)} ${pad(planned, 10)} avg ${pad(fmt(duration.avg), 6)} p95 ${pad(fmt(duration['p(95)']), 6)} p99 ${pad(fmt(duration['p(99)']), 6)} ` +
            `max ${pad(fmt(duration.max), 7)} failed ${pad(`${fmt((failed.rate || 0) * 100, 2)}%`, 8)} dropped ${fmt(dropped.count)}`);
    };
    phase('seed', 'seed', `${subscriptions}`);
    phase('renew', 'renew', `${renewals}`);
    phase('cancel', 'cancel', `${cancellations}`);
    phase('read', 'read', `${readVus} VUs`);

    const lag = metric(data, 'bigdata_reporting_invoice_lag');
    const consistency = metric(data, 'bigdata_time_to_consistency_seconds');
    const text = [
        '',
        `big-data run · profile ${profileName} · ${subscriptions} subscriptions, ${renewals} renewals, ${cancellations} cancellations`,
        `created ${fmt(metric(data, 'bigdata_subscriptions_created').count)} · renewals accepted ${fmt(metric(data, 'bigdata_renewals_accepted').count)}` +
        ` (retries ${fmt(metric(data, 'bigdata_renewal_retries').count)}) · canceled ${fmt(metric(data, 'bigdata_cancellations_accepted').count)}`,
        '',
        ...rows,
        '',
        `random page search   p95 ${fmt(metric(data, 'bigdata_search_random_page_duration')['p(95)'])} ms  max ${fmt(metric(data, 'bigdata_search_random_page_duration').max)} ms`,
        reportingUrl
            ? `reporting            invoices ${fmt(metric(data, 'bigdata_reporting_invoices').value)} · max lag ${fmt(lag.max)} invoices` +
            ` · caught up ${consistency.max === undefined ? 'never' : `${fmt(consistency.max)} s after the last renewal`}`
            : 'reporting            (REPORTING_URL not set: not monitored)',
        '',
    ].join('\n');
    return {stdout: text};
}
