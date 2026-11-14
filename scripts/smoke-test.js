// Smoke test: a few iterations over EVERY endpoint, including the error contract (problem+json). Run it before any
// heavier test: if this fails, the numbers of a load test mean nothing.
//   SMOKE_VUS (1) · SMOKE_ITERATIONS (3, per VU)
// Passes when every check passes, no request fails unexpectedly and p95 stays under 1 s.
import { check, group } from 'k6';
import http from 'k6/http';
import { BASE, MISSING_ID, api, envNumber, expectProblem, expectStatus, params, planBody, subscriptionBody, unique } from './lib/api.js';

export const options = {
  scenarios: {
    smoke: { executor: 'per-vu-iterations', vus: envNumber('SMOKE_VUS', 1), iterations: envNumber('SMOKE_ITERATIONS', 3), maxDuration: '2m' },
  },
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<1000'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  group('health', () => {
    for (const kind of ['live', 'ready', '']) {
      const res = api.health(kind);
      check(res, { [`health/${kind || 'all'} is UP`]: (r) => r.status === 200 && r.json('status') === 'UP' });
    }
  });

  group('plans', () => {
    const list = api.listPlans();
    check(list, { 'plans: seeded catalog is listed': (r) => r.status === 200 && r.json('items').some((p) => p.code === 'pro-monthly') });
    check(api.getPlan('pro-monthly'), { 'plans: get by code': (r) => r.status === 200 && r.json('code') === 'pro-monthly' });
    expectProblem(api.getPlan('does-not-exist', [404]), 404, 'plan-not-found');

    const code = unique('smoke').toLowerCase();
    check(api.createPlan(planBody(code)), { 'plans: create 201': (r) => r.status === 201 && r.json('code') === code });
    expectProblem(api.createPlan(planBody(code), [409]), 409, 'plan-already-exists');
    const invalid = api.createPlan(planBody(code, { currency: 'US', interval: 'month', amount: -1 }), [400]);
    expectProblem(invalid, 400, 'validation');
    check(invalid, { 'plans: validation lists every field': (r) => ['amount', 'currency', 'interval'].every((f) => f in (r.json('errors') || {})) });
  });

  group('subscriptions', () => {
    const created = api.createSubscription(subscriptionBody(unique('Smoke')));
    if (!check(created, {
      'subscriptions: create 201 Active': (r) => r.status === 201 && r.json('status') === 1,
      'subscriptions: Location header': (r) => String(r.headers.Location || '').endsWith(`/subscriptions/${r.json('id')}`),
    })) return;
    const id = created.json('id');

    // Correlation: an inbound X-Trace-Id is echoed back.
    const traced = http.get(`${BASE}/subscriptions/${id}`, Object.assign(params('GET /subscriptions/{id}'), { headers: { 'X-Trace-Id': 'smoke-trace-1' } }));
    check(traced, { 'subscriptions: get 200 + X-Trace-Id echoed': (r) => r.status === 200 && r.headers['X-Trace-Id'] === 'smoke-trace-1' });

    const invalid = api.createSubscription({ serviceName: '', price: 0, startDate: '2026-09-05', trialEndsAt: '2026-09-05' }, [400]);
    expectProblem(invalid, 400, 'validation');
    check(invalid, { 'subscriptions: validation lists every field': (r) => ['serviceName', 'price', 'trialEndsAt'].every((f) => f in (r.json('errors') || {})) });
    expectProblem(api.getSubscription(MISSING_ID, [404]), 404, 'subscription-not-found');
    expectStatus(http.get(`${BASE}/subscriptions/not-a-guid`, params('GET /subscriptions/{id}', [400])), 400, 'subscriptions: malformed id 400');

    check(api.pauseSubscription(id), { 'subscriptions: pause → Paused': (r) => r.status === 200 && r.json('status') === 3 });
    expectProblem(api.pauseSubscription(id, [409]), 409, 'invalid-subscription-transition');
    check(api.resumeSubscription(id), { 'subscriptions: resume → Active': (r) => r.status === 200 && r.json('status') === 1 });

    const renewed = api.renewSubscription(id);
    if (check(renewed, { 'subscriptions: renew issues an invoice': (r) => r.status === 200 && Boolean(r.json('invoice.id')) })) {
      const invoiceId = renewed.json('invoice.id');
      check(api.listInvoices(id), { 'invoices: listed for the subscription': (r) => r.status === 200 && r.json('items').some((i) => i.id === invoiceId) });
      check(api.getInvoice(invoiceId), { 'invoices: get 200 OPEN': (r) => r.status === 200 && r.json('statusLabel') === 'OPEN' });
      check(api.payInvoice(invoiceId), { 'invoices: pay → PAID': (r) => r.status === 200 && r.json('statusLabel') === 'PAID' });
      expectProblem(api.payInvoice(invoiceId, [409]), 409, 'invoice-not-payable');
    }
    expectProblem(api.getInvoice(MISSING_ID, [404]), 404, 'invoice-not-found');
    expectProblem(api.listInvoices(MISSING_ID, [404]), 404, 'subscription-not-found');

    check(api.searchSubscriptions({ status: 'ACTIVE', limit: 5 }), {
      'search: page of at most 5 active': (r) => r.status === 200 && r.json('items').length <= 5 && r.json('items').every((s) => s.status === 1),
    });
    expectProblem(api.searchSubscriptions({ status: 'active' }, [400]), 400, 'invalid-subscription-status');
    expectProblem(api.searchSubscriptions({ cursor: 'not-a-cursor' }, [400]), 400, 'invalid-cursor');

    expectStatus(api.cancelSubscription(id), 204, 'subscriptions: cancel 204');
    check(api.getSubscription(id), { 'subscriptions: canceled row is kept (status 2)': (r) => r.status === 200 && r.json('status') === 2 });
    expectProblem(api.cancelSubscription(id, [400]), 400, 'subscription-already-canceled');
  });
}
