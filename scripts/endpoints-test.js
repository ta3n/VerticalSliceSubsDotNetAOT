// Endpoint coverage under load: one flow per area runs in parallel, so EVERY route of the API (and, with S2S=true, of
// Billing and Payments) gets traffic, success paths and the documented errors alike. Answers: does any route break or
// slow down when all of them are used at once, and is any route left out of the load tests?
//   catalog    plans: list, get, create, duplicate (409), validation (400), unknown (404)
//   reads      cached get, search + keyset paging, invoice list/get on seeded data
//   lifecycle  create → get → pause → resume → renew → invoices → pay → cancel
//   invoices   the Relay's internal calls: settle (repeat, other payment 409), void (repeat, after payment 409), 404/400
//   errors     the problem+json contract: 404, 400, 409, 415, unknown route
//   health     live / ready / full
//   saga       S2S=true only: renewal collected (captured, declined, silent gateway) → payment + events, then cancel →
//              refund → credit note; failures are counted in saga_failures
// Knobs: ENDPOINTS_VUS (40, HTTP flows in total) · ENDPOINTS_DURATION (1m) · ENDPOINTS_SAGA_VUS (5)
//        S2S (false; true needs Billing + Payments + Relay: Aspire) · RENEWAL_TIMEOUT_S (30, = Billing:RenewalTimeoutSeconds)
//        BASE_URL / BILLING_URL / PAYMENTS_URL / HTTP_TIMEOUT as in the other scripts.
// Passes when every check passes, <1% of requests fail, p95 < 1 s and every route was called at least once (calls_*).
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import {
  BASE, MISSING_ID, api, env, envNumber, expectProblem, expectStatus, fullLifecycle, params, pick, planBody,
  seedSubscriptions, subscriptionBody, trackCalls, unique,
} from './lib/api.js';

const vus = envNumber('ENDPOINTS_VUS', 40);
const duration = env('ENDPOINTS_DURATION', '1m');
const sagaVus = envNumber('ENDPOINTS_SAGA_VUS', 5);
const s2s = env('S2S', 'false') === 'true';
const renewalTimeout = envNumber('RENEWAL_TIMEOUT_S', 30);
if (!Number.isFinite(vus) || vus < 5) throw new Error('ENDPOINTS_VUS must be at least 5');
if (!Number.isFinite(renewalTimeout) || renewalTimeout <= 0) throw new Error('RENEWAL_TIMEOUT_S must be positive');
const settleSeconds = renewalTimeout + 30;

const sagaSettle = new Trend('saga_settle_ms', true);
const sagaFailures = new Counter('saga_failures');
const S2S_ROUTES = ['startRenewal', 'getInvoiceRenewal', 'getPayment', 'getPaymentEvents', 'listCreditNotes'];
const TERMINAL = ['Completed', 'Declined', 'TimedOut', 'Compensated'];

// Every api.* call is counted (calls_<name>); a route without a single call fails the run.
const required = trackCalls().filter((name) => s2s || !S2S_ROUTES.includes(name));

const share = (weight) => Math.max(1, Math.round(vus * weight));
const flow = (weight, exec) => ({ executor: 'constant-vus', vus: share(weight), duration, exec });

export const options = {
  scenarios: Object.assign(
    {
      catalog: flow(0.15, 'catalog'),
      reads: flow(0.3, 'reads'),
      lifecycle: flow(0.2, 'lifecycleFlow'),
      invoices: flow(0.15, 'invoiceFlow'),
      errors: flow(0.1, 'errors'),
      health: { executor: 'constant-vus', vus: 1, duration, exec: 'health' },
    },
    s2s
      ? {
        saga: {
          executor: 'constant-vus', vus: sagaVus, duration, exec: 'sagaFlow',
          // An iteration can be waiting for the saga timeout; let it finish instead of cutting it off.
          gracefulStop: `${settleSeconds + 60}s`,
        },
      }
      : {},
  ),
  thresholds: Object.assign(
    {
      checks: ['rate>0.99'],
      http_req_failed: ['rate<0.01'],
      http_req_duration: ['p(95)<1000'],
    },
    s2s ? { saga_failures: ['count==0'] } : {},
    Object.fromEntries(required.map((name) => [`calls_${name.toLowerCase()}`, ['count>0']])),
  ),
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'p(99)', 'max'],
};

const today = () => new Date().toISOString().slice(0, 10);
const uuid = () => 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
  const r = Math.floor(Math.random() * 16);
  return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
});

export function setup() {
  const ids = seedSubscriptions(100, 'Endpoints-seed');
  // A few invoices for the read flow; renewing is synchronous and needs no other service.
  const invoiceIds = [];
  for (const id of ids.slice(0, 20)) {
    const renewed = api.renewSubscription(id);
    if (renewed.status !== 200) throw new Error(`seeding invoices failed: ${renewed.status} ${renewed.body}`);
    invoiceIds.push(renewed.json('invoice.id'));
  }
  return { ids, invoiceIds };
}

export function catalog() {
  const list = api.listPlans();
  check(list, { 'plans: seeded catalog is listed': (r) => r.status === 200 && r.json('items').some((p) => p.code === 'pro-monthly') });
  check(api.getPlan('pro-monthly'), { 'plans: get by code': (r) => r.status === 200 && r.json('code') === 'pro-monthly' });
  expectProblem(api.getPlan('does-not-exist', [404]), 404, 'plan-not-found');

  const code = unique('ep').toLowerCase();
  check(api.createPlan(planBody(code)), { 'plans: create 201': (r) => r.status === 201 && r.json('code') === code });
  check(api.getPlan(code), { 'plans: created plan can be read': (r) => r.status === 200 && r.json('code') === code });
  expectProblem(api.createPlan(planBody(code), [409]), 409, 'plan-already-exists');
  expectProblem(api.createPlan(planBody(unique('bad').toLowerCase(), { currency: 'US', amount: -1 }), [400]), 400, 'validation');
}

export function reads(data) {
  const roll = Math.random();
  if (roll < 0.35) {
    expectStatus(api.getSubscription(pick(data.ids)), 200, 'reads: subscription 200');
  } else if (roll < 0.55) {
    const page = api.searchSubscriptions({ status: pick(['ACTIVE', 'PAUSED', null]), limit: 20 });
    expectStatus(page, 200, 'reads: search 200');
    const cursor = page.status === 200 ? page.json('nextCursor') : null;
    if (cursor) expectStatus(api.searchSubscriptions({ limit: 20, cursor }), 200, 'reads: search next page 200');
  } else if (roll < 0.75) {
    expectStatus(api.listInvoices(pick(data.ids)), 200, 'reads: invoices of a subscription 200');
  } else {
    const invoiceId = pick(data.invoiceIds);
    check(api.getInvoice(invoiceId), { 'reads: invoice 200': (r) => r.status === 200 && r.json('id') === invoiceId });
  }
}

export function lifecycleFlow() {
  fullLifecycle('Endpoints');
}

/** The Relay's calls into the API for the renewal saga, driven directly so the lock/idempotency rules run under load. */
export function invoiceFlow() {
  const created = api.createSubscription(subscriptionBody(unique('Endpoints-invoice')));
  if (!expectStatus(created, 201, 'invoices: create 201')) return;
  const subscriptionId = created.json('id');

  const first = api.renewSubscription(subscriptionId);
  if (!expectStatus(first, 200, 'invoices: renew 200')) return;
  const settled = first.json('invoice.id');
  const paymentId = uuid();
  check(api.settleInvoice(settled, { paymentId }), { 'invoices: settle → PAID': (r) => r.status === 200 && r.json('statusLabel') === 'PAID' });
  check(api.settleInvoice(settled, { paymentId }), { 'invoices: repeated settle is a no-op': (r) => r.status === 200 && r.json('statusLabel') === 'PAID' });
  expectProblem(api.settleInvoice(settled, { paymentId: uuid() }, [409]), 409, 'invoice-not-payable');
  expectProblem(api.voidInvoice(settled, [409]), 409, 'invoice-not-voidable');

  const second = api.renewSubscription(subscriptionId);
  if (!expectStatus(second, 200, 'invoices: second renew 200')) return;
  const voided = second.json('invoice.id');
  check(api.voidInvoice(voided), { 'invoices: void → VOID': (r) => r.status === 200 && r.json('statusLabel') === 'VOID' });
  check(api.voidInvoice(voided), { 'invoices: repeated void is a no-op': (r) => r.status === 200 && r.json('statusLabel') === 'VOID' });
  expectProblem(api.payInvoice(voided, [409]), 409, 'invoice-not-payable');

  expectProblem(api.settleInvoice(MISSING_ID, { paymentId: uuid() }, [404]), 404, 'invoice-not-found');
  expectProblem(api.voidInvoice(MISSING_ID, [404]), 404, 'invoice-not-found');
  expectProblem(api.settleInvoice(voided, {}, [400]), 400, 'validation');
  api.cancelSubscription(subscriptionId);
}

/** The error contract: every response is RFC 9457 problem+json with the stable type slug, and stays cheap. */
export function errors() {
  expectProblem(api.getSubscription(MISSING_ID, [404]), 404, 'subscription-not-found');
  expectProblem(api.pauseSubscription(MISSING_ID, [404]), 404, 'subscription-not-found');
  expectProblem(api.cancelSubscription(MISSING_ID, [404]), 404, 'subscription-not-found');
  expectProblem(api.getInvoice(MISSING_ID, [404]), 404, 'invoice-not-found');
  expectProblem(api.listInvoices(MISSING_ID, [404]), 404, 'subscription-not-found');
  expectProblem(api.createSubscription({ serviceName: '', price: 0, startDate: today() }, [400]), 400, 'validation');
  expectProblem(api.searchSubscriptions({ status: 'active' }, [400]), 400, 'invalid-subscription-status');
  expectProblem(api.searchSubscriptions({ cursor: 'not-a-cursor' }, [400]), 400, 'invalid-cursor');
  expectStatus(http.get(`${BASE}/subscriptions/not-a-guid`, params('GET /subscriptions/{id}', [400])), 400, 'errors: malformed id 400');
  expectStatus(http.get(`${BASE}/no-such-route`, params('GET /no-such-route', [404])), 404, 'errors: unknown route 404');
  const notJson = Object.assign(params('POST /subscriptions', [415]), { headers: { 'Content-Type': 'text/plain' } });
  expectStatus(http.post(`${BASE}/subscriptions`, 'serviceName=x', notJson), 415, 'errors: non-JSON body 415');

  const created = api.createSubscription(subscriptionBody(unique('Endpoints-errors')));
  if (created.status !== 201) return;
  const id = created.json('id');
  api.pauseSubscription(id);
  expectProblem(api.pauseSubscription(id, [409]), 409, 'invalid-subscription-transition');
  expectStatus(api.cancelSubscription(id), 204, 'errors: cancel 204');
  expectProblem(api.cancelSubscription(id, [400]), 400, 'subscription-already-canceled');
}

export function health() {
  for (const kind of ['live', 'ready', '']) {
    check(api.health(kind), { [`health/${kind || 'all'} is UP`]: (r) => r.status === 200 && r.json('status') === 'UP' });
  }
  sleep(1);
}

// ---------------------------------------------------------------- saga (S2S=true)
function untilTerminal(invoiceId) {
  const deadline = Date.now() + settleSeconds * 1000;
  while (Date.now() < deadline) {
    // 404 until Billing has consumed the invoice's InvoiceIssued message.
    const response = api.getInvoiceRenewal(invoiceId, [200, 404]);
    if (response.status === 200 && TERMINAL.includes(response.json('state'))) return response.json();
    sleep(0.5);
  }
  return null;
}

function until(probe) {
  const deadline = Date.now() + settleSeconds * 1000;
  while (Date.now() < deadline) {
    const value = probe();
    if (value) return value;
    sleep(0.5);
  }
  return null;
}

function sagaFailed(what) {
  check(false, { [`saga: ${what}`]: (ok) => ok });
  sagaFailures.add(1);
}

/** 60% captured, 30% declined, 10% silent gateway (waits for the saga timeout); half of the captured ones are canceled. */
export function sagaFlow() {
  sagaFailures.add(0);
  const slot = __ITER % 10;
  const kind = slot < 6 ? 'captured' : slot < 9 ? 'declined' : 'silent';
  const price = { captured: 10, declined: 6000, silent: 9500 }[kind];
  const expected = { captured: 'Completed', declined: 'Declined', silent: 'TimedOut' }[kind];

  const created = api.createSubscription(subscriptionBody(unique(`Endpoints-saga-${kind}`), { price, startDate: today(), trialEndsAt: null }));
  if (!expectStatus(created, 201, 'saga: create 201')) return sagaFailed('create');
  const subscriptionId = created.json('id');
  const started = api.startRenewal(subscriptionId);
  if (!expectStatus(started, 202, 'saga: renewal accepted 202')) return sagaFailed('renewal not accepted');
  const invoiceId = started.json('invoice.id');

  const begun = Date.now();
  const status = untilTerminal(invoiceId);
  sagaSettle.add(Date.now() - begun, { kind });
  if (!check(status, { [`saga ${kind}: reached ${expected}`]: (s) => s !== null && s.state === expected })) return sagaFailed(`${kind} did not reach ${expected}`);

  const invoiceStatus = { captured: 'PAID', declined: 'VOID', silent: 'VOID' }[kind];
  const invoice = until(() => {
    const response = api.getInvoice(invoiceId);
    return response.status === 200 && response.json('statusLabel') === invoiceStatus ? response : null;
  });
  if (!check(invoice, { [`saga ${kind}: invoice ${invoiceStatus}`]: (r) => r !== null })) return sagaFailed(`${kind} invoice is not ${invoiceStatus}`);

  const paymentId = status.paymentId;
  const payment = api.getPayment(paymentId);
  check(payment, { 'saga: payment 200': (r) => r.status === 200 && r.json('id') === paymentId });
  const events = api.getPaymentEvents(paymentId);
  check(events, { 'saga: payment events 200': (r) => r.status === 200 && r.json('items').length >= 1 });

  if (kind === 'captured' && __ITER % 2 === 0) {
    expectStatus(api.cancelSubscription(subscriptionId), 204, 'saga: cancel 204');
    const notes = until(() => {
      const response = api.listCreditNotes(subscriptionId);
      return response.status === 200 && response.json('items').length === 1 ? response : null;
    });
    if (!check(notes, { 'saga: refund issued exactly one credit note': (r) => r !== null && r.json('items.0.paymentId') === paymentId })) return sagaFailed('no credit note after cancel');
    const refunded = until(() => {
      const response = api.getPayment(paymentId);
      return response.status === 200 && response.json('status') === 'Refunded' ? response : null;
    });
    check(refunded, { 'saga: payment is Refunded': (r) => r !== null });
    if (refunded === null) sagaFailed('payment not refunded');
  }
}
