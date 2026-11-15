// Renewal orchestration (capture, decline, timeout), then cancel/refund choreography on the captured payment.
//   ITERATIONS (3) · RENEWAL_TIMEOUT_S (30, must match Billing:RenewalTimeoutSeconds)
//   BASE_URL (http://localhost:8080) · BILLING_URL (http://localhost:5128, HTTP not gRPC)
//   PAYMENTS_URL (http://localhost:5129) · HTTP_TIMEOUT (10s)
// Each iteration uses fresh subscriptions and current billing dates; existing data can stay in place between runs.
// Logs contain traceId + invoiceId for Grafana's Request flow dashboard. Polls get a 30 s delivery margin.
import { check, fail, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { api, envNumber, subscriptionBody, unique } from './lib/api.js';

const iterations = envNumber('ITERATIONS', 3);
const renewalTimeout = envNumber('RENEWAL_TIMEOUT_S', 30);
if (!Number.isInteger(iterations) || iterations < 1) throw new Error('ITERATIONS must be a positive integer');
if (!Number.isFinite(renewalTimeout) || renewalTimeout <= 0) throw new Error('RENEWAL_TIMEOUT_S must be positive');
const settleSeconds = renewalTimeout + 30;
const sagaSettle = new Trend('saga_settle_ms', true);
const failures = new Counter('saga_failures');

export const options = {
  scenarios: {
    saga: { executor: 'per-vu-iterations', vus: 1, iterations, maxDuration: `${iterations * (4 * settleSeconds + 90)}s` },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate==0'],
    saga_failures: ['count==0'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'p(99)', 'max'],
};

function abort(label, detail) {
  check(false, { [label]: (ok) => ok });
  failures.add(1);
  fail(`${label}: ${detail}`);
}

function requireCheck(label, condition) {
  if (!condition) abort(label, 'condition was not met');
  check(true, { [label]: (ok) => ok });
}

function readJson(response, label) {
  let body;
  try {
    body = response.json();
  } catch (_) {
    abort(label, `invalid JSON: ${String(response.body).slice(0, 500)}`);
  }
  if (!body || typeof body !== 'object') abort(label, `expected a JSON object: ${JSON.stringify(body)}`);
  return body;
}

function requireStatus(response, status, label) {
  if (response.status !== status) abort(label, `HTTP ${response.status}: ${response.error || response.body}`);
  check(true, { [label]: (ok) => ok });
}

// Check once per outcome, so intermediate states do not dilute a failed business assertion.
function poll(label, request, ready, deadline, allowNotFound = false) {
  let last;
  do {
    const response = request();
    if (response.status === 200) {
      last = readJson(response, label);
      if (ready(last)) {
        check(true, { [label]: (ok) => ok });
        return last;
      }
    } else if (allowNotFound && response.status === 404) {
      last = 'HTTP 404 (not consumed yet)';
    } else {
      abort(label, `HTTP ${response.status}: ${response.error || response.body}`);
    }
    if (Date.now() >= deadline) break;
    sleep(0.5);
  } while (Date.now() < deadline);
  abort(label, `did not settle within ${settleSeconds}s; last response: ${JSON.stringify(last)}`);
}

function trace() {
  const hex = (length) => Array.from({ length }, () => Math.floor(Math.random() * 16).toString(16)).join('');
  const traceId = `1${hex(31)}`;
  return { traceId, headers: { traceparent: `00-${traceId}-1${hex(15)}-01` } };
}

function paymentEvents(paymentId, expected, deadline, label) {
  return poll(label, () => api.getPaymentEvents(paymentId, [200, 404]),
    (body) => Array.isArray(body.items) && body.items.length === expected.length
      && body.items.every((event, i) => event && event.type === expected[i]),
    deadline, true);
}

function renewal(label, price, state, invoiceStatus, eventTypes) {
  const created = api.createSubscription(subscriptionBody(unique(`Saga-${label}`), {
    price, startDate: new Date().toISOString().slice(0, 10), trialEndsAt: null,
  }));
  requireStatus(created, 201, `${label}: create 201`);
  const subscriptionId = readJson(created, label).id;
  requireCheck(`${label}: subscription id`, Boolean(subscriptionId));

  const context = trace();
  const started = Date.now();
  const issued = api.startRenewal(subscriptionId, undefined, context.headers);
  requireStatus(issued, 202, `${label}: renewal accepted 202`);
  const invoice = readJson(issued, label).invoice;
  requireCheck(`${label}: invoice belongs to subscription`, invoice && invoice.id && invoice.subscriptionId === subscriptionId);
  const invoiceId = invoice.id;
  console.log(JSON.stringify({ case: label, traceId: context.traceId, invoiceId, subscriptionId }));
  const deadline = started + settleSeconds * 1000;
  const status = poll(`${label}: saga ${state}`, () => api.getInvoiceRenewal(invoiceId, [200, 404]), (body) => {
    if (['Completed', 'Declined', 'TimedOut', 'Compensated'].includes(body.state) && body.state !== state) {
      abort(`${label}: saga ${state}`, `unexpected terminal state ${JSON.stringify(body)}`);
    }
    return body.state === state;
  }, deadline, true);
  sagaSettle.add(Date.now() - started, { case: label });
  requireCheck(`${label}: saga identity`, status.invoiceId === invoiceId && Boolean(status.paymentId));

  poll(`${label}: invoice ${invoiceStatus}`, () => api.getInvoice(invoiceId),
    (body) => body.id === invoiceId && body.statusLabel === invoiceStatus, deadline);
  if (state !== 'Completed') {
    poll(`${label}: subscription PAUSED`, () => api.getSubscription(subscriptionId),
      (body) => body.id === subscriptionId && body.status === 3, deadline);
  }
  if (eventTypes) {
    const events = paymentEvents(status.paymentId, eventTypes, deadline, `${label}: payment event sequence`);
    const charge = events.items[0].data;
    requireCheck(`${label}: charge matches invoice`, charge && charge.invoiceId === invoiceId
      && charge.subscriptionId === subscriptionId && charge.amount === invoice.amount && charge.currency === invoice.currency);
  } else {
    poll(`${label}: stream ends ChargeCanceled`, () => api.getPaymentEvents(status.paymentId, [200, 404]),
      (body) => Array.isArray(body.items) && body.items.length > 0
        && body.items[body.items.length - 1] && body.items[body.items.length - 1].type === 'ChargeCanceled',
      deadline, true);
  }
  console.log(JSON.stringify({ case: label, state, traceId: context.traceId, invoiceId, paymentId: status.paymentId }));
  return { subscriptionId, invoiceId, paymentId: status.paymentId, invoice };
}

export default function () {
  failures.add(0);
  let captured;
  group('success', () => { captured = renewal('success', 10, 'Completed', 'PAID', ['ChargeRequested', 'ChargeCaptured']); });
  group('decline', () => { renewal('decline', 6000, 'Declined', 'VOID', ['ChargeRequested', 'ChargeDeclined']); });
  group('timeout', () => { renewal('timeout', 9500, 'TimedOut', 'VOID'); });
  group('cancel/refund', () => {
    const context = trace();
    console.log(JSON.stringify({ case: 'refund', traceId: context.traceId, invoiceId: captured.invoiceId, paymentId: captured.paymentId }));
    requireStatus(api.cancelSubscription(captured.subscriptionId, undefined, context.headers), 204, 'refund: cancel 204');
    const deadline = Date.now() + settleSeconds * 1000;
    const notes = poll('refund: exactly one credit note', () => api.listCreditNotes(captured.subscriptionId),
      (body) => Array.isArray(body.items) && body.items.length === 1, deadline);
    const note = notes.items[0];
    requireCheck('refund: credit note matches captured invoice', note && note.paymentId === captured.paymentId
      && note.invoiceId === captured.invoiceId && note.subscriptionId === captured.subscriptionId
      && note.amount === captured.invoice.amount && note.currency === captured.invoice.currency);
    paymentEvents(captured.paymentId, ['ChargeRequested', 'ChargeCaptured', 'ChargeRefunded'], deadline, 'refund: stream ends ChargeRefunded');
    poll('refund: payment snapshot Refunded', () => api.getPayment(captured.paymentId),
      (body) => body.id === captured.paymentId && body.status === 'Refunded', deadline);
  });
}
