// Shared building blocks for the k6 scenarios in scripts/*-test.js: one function per API endpoint, check helpers and
// the mixed workload used by the spike / soak / breakpoint tests.
//
// Every request carries an `endpoint` tag with the route template (e.g. "GET /subscriptions/{id}"), so results can be
// broken down per endpoint without the high-cardinality `url`/`name` tags (they contain entity ids).
// A request whose non-2xx status is the *correct* outcome passes the statuses it expects, so it does not count as
// http_req_failed.
import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

function withoutTrailingSlashes(url) {
  let end = url.length;
  while (end > 0 && url[end - 1] === '/') end--;
  return url.slice(0, end);
}

export const BASE = withoutTrailingSlashes(__ENV.BASE_URL || 'http://localhost:8080');
const JSON_HEADERS = { 'Content-Type': 'application/json' };
const TIMEOUT = __ENV.HTTP_TIMEOUT || '10s';

/** Wall-clock time of one create → pause → resume → renew → pay → cancel journey. */
export const lifecycleDuration = new Trend('lifecycle_duration', true);

/** Request params: JSON headers, the `endpoint` tag and, for expected error responses, the accepted statuses. */
export function params(endpoint, expected) {
  const p = { headers: JSON_HEADERS, tags: { endpoint }, timeout: TIMEOUT };
  if (expected) p.responseCallback = http.expectedStatuses(...expected);
  return p;
}

const json = (value) => JSON.stringify(value);

/** One function per endpoint. `expect` = statuses that are the correct outcome when it is not a 2xx. */
export const api = {
  createSubscription: (body, expect) => http.post(`${BASE}/subscriptions`, json(body), params('POST /subscriptions', expect)),
  getSubscription: (id, expect) => http.get(`${BASE}/subscriptions/${id}`, params('GET /subscriptions/{id}', expect)),
  cancelSubscription: (id, expect) => http.del(`${BASE}/subscriptions/${id}`, null, params('DELETE /subscriptions/{id}', expect)),
  pauseSubscription: (id, expect) => http.post(`${BASE}/subscriptions/${id}/pause`, null, params('POST /subscriptions/{id}/pause', expect)),
  resumeSubscription: (id, expect) => http.post(`${BASE}/subscriptions/${id}/resume`, null, params('POST /subscriptions/{id}/resume', expect)),
  renewSubscription: (id, expect) => http.post(`${BASE}/subscriptions/${id}/renew`, null, params('POST /subscriptions/{id}/renew', expect)),
  searchSubscriptions: (query, expect) => http.post(`${BASE}/subscriptions/search`, json(query || {}), params('POST /subscriptions/search', expect)),
  listInvoices: (subscriptionId, expect) => http.get(`${BASE}/subscriptions/${subscriptionId}/invoices`, params('GET /subscriptions/{id}/invoices', expect)),
  getInvoice: (id, expect) => http.get(`${BASE}/invoices/${id}`, params('GET /invoices/{id}', expect)),
  payInvoice: (id, expect) => http.post(`${BASE}/invoices/${id}/pay`, null, params('POST /invoices/{id}/pay', expect)),
  listPlans: () => http.get(`${BASE}/plans`, params('GET /plans')),
  getPlan: (code, expect) => http.get(`${BASE}/plans/${code}`, params('GET /plans/{code}', expect)),
  createPlan: (body, expect) => http.post(`${BASE}/plans`, json(body), params('POST /plans', expect)),
  /** kind: 'live' | 'ready' | '' (everything). */
  health: (kind) => http.get(`${BASE}/health${kind ? `/${kind}` : ''}`, params(`GET /health${kind ? `/${kind}` : ''}`)),
};

/** `count` identical requests for http.batch (used to race the same operation). */
export function race(count, method, path, endpoint, expected, body) {
  const request = { method, url: `${BASE}${path}`, body: body === undefined ? null : json(body), params: params(endpoint, expected) };
  return Array.from({ length: count }, () => request);
}

// ---------------------------------------------------------------- data
const MISSING_ID = '00000000-0000-0000-0000-000000000000';

/** Unique per VU/iteration, safe as a service name or plan code. */
export function unique(prefix) {
  return `${prefix}-${__VU}-${__ITER}-${Date.now().toString(36)}${Math.floor(Math.random() * 1296).toString(36)}`;
}

export function subscriptionBody(serviceName, overrides) {
  return Object.assign(
    { serviceName, price: 9900 + Math.floor(Math.random() * 100), startDate: '2026-09-05', trialEndsAt: '2026-09-12' },
    overrides);
}

export function planBody(code, overrides) {
  return Object.assign({ code, name: `Plan ${code}`, amount: 49, currency: 'USD', interval: 'MONTH', trialDays: 7 }, overrides);
}

export const pick = (items) => items[Math.floor(Math.random() * items.length)];

/** "90s" / "5m" / "1h" → seconds. */
export function seconds(duration) {
  const match = /^(\d+(?:\.\d+)?)(ms|s|m|h)$/.exec(String(duration).trim());
  if (!match) throw new Error(`unsupported duration '${duration}' (use e.g. 30s, 5m, 1h)`);
  return Number(match[1]) * { ms: 0.001, s: 1, m: 60, h: 3600 }[match[2]];
}

export const env = (name, fallback) => __ENV[name] || fallback;
export const envNumber = (name, fallback) => Number(__ENV[name] || fallback);

// ---------------------------------------------------------------- checks
/** Named status check; returns whether it passed. */
export function expectStatus(res, status, name) {
  return check(res, { [name || `status ${status}`]: (r) => r.status === status });
}

/** RFC 9457 problem with the given status and type slug (https://api.demo/errors/<slug>). */
export function expectProblem(res, status, slug) {
  return check(res, {
    [`${slug} → ${status} problem+json`]: (r) => {
      if (r.status !== status || !String(r.headers['Content-Type'] || '').startsWith('application/problem+json')) return false;
      const body = r.json();
      return body.type === `https://api.demo/errors/${slug}` && body.status === status && Boolean(body.traceId) && Boolean(body.title);
    },
  });
}

// ---------------------------------------------------------------- journeys
/** Creates `count` subscriptions (in batches) and returns their ids; for setup(). */
export function seedSubscriptions(count, label, bodyOf) {
  const ids = [];
  for (let offset = 0; offset < count; offset += 20) {
    const size = Math.min(20, count - offset);
    const requests = Array.from({ length: size }, (_, i) => ({
      method: 'POST',
      url: `${BASE}/subscriptions`,
      body: json(bodyOf ? bodyOf(offset + i) : subscriptionBody(`${label}-${offset + i}`)),
      params: params('POST /subscriptions'),
    }));
    for (const res of http.batch(requests)) {
      if (res.status !== 201) throw new Error(`seeding failed: ${res.status} ${res.body}`);
      ids.push(res.json('id'));
    }
  }
  return ids;
}

/** The whole customer journey on a fresh subscription. Returns false as soon as a step fails. */
export function fullLifecycle(label) {
  const started = Date.now();
  const created = api.createSubscription(subscriptionBody(unique(label || 'Lifecycle')));
  if (!expectStatus(created, 201, 'lifecycle: create 201')) return false;
  const id = created.json('id');

  let ok = expectStatus(api.getSubscription(id), 200, 'lifecycle: get 200');
  ok = expectStatus(api.pauseSubscription(id), 200, 'lifecycle: pause 200') && ok;
  ok = expectStatus(api.resumeSubscription(id), 200, 'lifecycle: resume 200') && ok;
  const renewed = api.renewSubscription(id);
  ok = expectStatus(renewed, 200, 'lifecycle: renew 200') && ok;
  if (renewed.status === 200) {
    const invoiceId = renewed.json('invoice.id');
    ok = expectStatus(api.listInvoices(id), 200, 'lifecycle: invoices 200') && ok;
    ok = expectStatus(api.payInvoice(invoiceId), 200, 'lifecycle: pay 200') && ok;
  }
  ok = expectStatus(api.cancelSubscription(id), 204, 'lifecycle: cancel 204') && ok;
  lifecycleDuration.add(Date.now() - started);
  return ok;
}

/**
 * Production-like traffic mix on top of a pool of existing subscriptions (data.ids from seedSubscriptions):
 *   30% list plans · 5% get plan · 25% cached read · 5% invoices · 10% search · 15% create · 7% full lifecycle ·
 *   3% client mistakes (404 / validation), which must stay cheap and well-formed.
 */
export function mixedWorkload(data) {
  const roll = Math.random() * 100;
  if (roll < 30) {
    expectStatus(api.listPlans(), 200, 'plans 200');
  } else if (roll < 35) {
    expectStatus(api.getPlan('pro-monthly'), 200, 'plan 200');
  } else if (roll < 60) {
    expectStatus(api.getSubscription(pick(data.ids)), 200, 'get 200');
  } else if (roll < 65) {
    expectStatus(api.listInvoices(pick(data.ids)), 200, 'invoices 200');
  } else if (roll < 75) {
    const page = api.searchSubscriptions({ status: Math.random() < 0.5 ? 'ACTIVE' : null, limit: 20 });
    expectStatus(page, 200, 'search 200');
    const cursor = page.status === 200 ? page.json('nextCursor') : null;
    if (cursor && Math.random() < 0.3) {
      expectStatus(api.searchSubscriptions({ limit: 20, cursor }), 200, 'search next page 200');
    }
  } else if (roll < 90) {
    expectStatus(api.createSubscription(subscriptionBody(unique('Mixed'))), 201, 'create 201');
  } else if (roll < 97) {
    fullLifecycle('Mixed');
  } else if (roll < 98.5) {
    expectProblem(api.getSubscription(MISSING_ID, [404]), 404, 'subscription-not-found');
  } else {
    expectProblem(api.createSubscription({ serviceName: '', price: 0, startDate: '2026-09-05' }, [400]), 400, 'validation');
  }
}

export { MISSING_ID };
