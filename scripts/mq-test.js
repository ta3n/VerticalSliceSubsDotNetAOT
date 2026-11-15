// Message-broker load: drives the renewal and cancellation flows through RabbitMQ so every queue sees thousands of messages.
//   MQ_FLOWS (2500)      flows per type; four types run in parallel (22 × MQ_FLOWS messages over 11 queues = 5000 per queue on average)
//   MQ_VUS (100)         VUs per flow type: the pressure on the API; the queues' backlog shows how fast the consumers keep up
//   MQ_SETTLE_S (180)    the run waits this long after the last flow so the delayed saga timeouts fire (Wolverine delivers thousands of
//                        30 s timers over 2-3 minutes, not exactly on time)
//   BASE_URL (http://localhost:8080) · HTTP_TIMEOUT (10s)
//
// Messages per flow (n = MQ_FLOWS), all through the real chain API → outbox → Relay → RabbitMQ → Billing/Payments:
//   success   price 10    InvoiceIssued, ChargePayment, PaymentSucceeded, SettleInvoice, InvoiceSettled,
//                         then the subscription is canceled once PAID: SubscriptionCanceled, PaymentRefunded
//   decline   price 6000  InvoiceIssued, ChargePayment, PaymentFailed, CancelRenewal
//   timeout   price 9500  InvoiceIssued, ChargePayment, CancelPayment, CancelRenewal (after the saga timeout)
//   rejected  price 10    InvoiceIssued, ChargePayment, PaymentSucceeded, SettleInvoice, InvoiceSettlementRejected,
//                         CancelPayment, PaymentRefunded (the invoice is paid by hand right after the renewal starts, so the
//                         saga's settle is refused and its captured payment is compensated)
//
//   messages per queue    4n  InvoiceIssued, ChargePayment
//                         2n  PaymentSucceeded, SettleInvoice, CancelRenewal, CancelPayment, PaymentRefunded
//                         n   InvoiceSettled, InvoiceSettlementRejected, PaymentFailed, SubscriptionCanceled
//
// The `rejected` race is decided by timing: when the saga settles first the flow ends as a plain success, so the
// InvoiceSettlementRejected queue can receive a little less than n. Read the exact counts on the RabbitMQ queues.
import {check, sleep} from 'k6';
import {api, envNumber, subscriptionBody, unique} from './lib/api.js';

const flows = envNumber('MQ_FLOWS', 2500);
const vus = envNumber('MQ_VUS', 100);
const settleSeconds = envNumber('MQ_SETTLE_S', 180);
if (!Number.isInteger(flows) || flows < 1) throw new Error('MQ_FLOWS must be a positive integer');
if (!Number.isInteger(vus) || vus < 1) throw new Error('MQ_VUS must be a positive integer');
if (!Number.isFinite(settleSeconds) || settleSeconds < 0) throw new Error('MQ_SETTLE_S must not be negative');

const flow = (exec) => ({executor: 'shared-iterations', vus, iterations: flows, maxDuration: '30m', exec});

export const options = {
    scenarios: {
        success: flow('success'),
        decline: flow('decline'),
        timeout: flow('timeout'),
        rejected: flow('rejected'),
    },
    teardownTimeout: `${Math.ceil(settleSeconds) + 30}s`,
    thresholds: {
        checks: ['rate>0.99'],
        http_req_failed: ['rate<0.01'],
    },
    summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

const today = () => new Date().toISOString().slice(0, 10);

/** Creates a subscription and starts its renewal; returns the ids, or null when a step failed (already recorded as a failed check). */
function renew(label, price) {
    const created = api.createSubscription(subscriptionBody(unique(`Mq-${label}`), {
        price, startDate: today(), trialEndsAt: null,
    }));
    if (!check(created, {[`${label}: create 201`]: (r) => r.status === 201})) return null;
    const subscriptionId = created.json('id');

    const started = api.startRenewal(subscriptionId);
    if (!check(started, {[`${label}: renewal accepted 202`]: (r) => r.status === 202})) return null;
    return {subscriptionId, invoiceId: started.json('invoice.id')};
}

/** Polls the invoice until the saga has settled it (PAID); false when it did not within the settle window. */
function waitUntilPaid(invoiceId) {
    const deadline = Date.now() + (settleSeconds + 30) * 1000;
    do {
        const response = api.getInvoice(invoiceId);
        if (response.status === 200 && response.json('statusLabel') === 'PAID') return true;
        sleep(0.25);
    } while (Date.now() < deadline);
    return false;
}

export function success() {
    const renewal = renew('success', 10);
    if (!renewal) return;
    if (!check(waitUntilPaid(renewal.invoiceId), {'success: invoice PAID': (paid) => paid})) return;
    // Canceling a paid subscription starts the refund: SubscriptionCanceled → Payments → PaymentRefunded → credit note.
    check(api.cancelSubscription(renewal.subscriptionId), {'success: cancel 204': (r) => r.status === 204});
}

export function decline() {
    renew('decline', 6000);
}

export function timeout() {
    renew('timeout', 9500);
}

export function rejected() {
    const renewal = renew('rejected', 10);
    if (!renewal) return;
    // 200 = paid before the saga settled (the settle will be rejected); 409 = the saga was faster.
    check(api.payInvoice(renewal.invoiceId, [200, 409]), {'rejected: pay 200/409': (r) => r.status === 200 || r.status === 409});
}

// Keeps the run (and the k6 metrics) alive until the delayed saga timeouts have fired and the queues have drained.
export function teardown() {
    sleep(settleSeconds);
}
