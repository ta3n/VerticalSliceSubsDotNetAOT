// k6 load test against the running stack (podman compose up). Run through scripts/load-test.sh.
import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const JSON_HDR = { headers: { 'Content-Type': 'application/json' } };
const lifecycle = new Trend('lifecycle_duration', true);

const dur = __ENV.DURATION || '20s';
const vus = Number(__ENV.VUS || 50);

export const options = {
  scenarios: {
    read_plans:   { executor: 'constant-vus', vus, duration: dur, exec: 'readPlans', startTime: '0s' },
    read_cached:  { executor: 'constant-vus', vus, duration: dur, exec: 'readSubscription', startTime: `${parseInt(dur) + 5}s` },
    create:       { executor: 'constant-vus', vus, duration: dur, exec: 'createSubscription', startTime: `${(parseInt(dur) + 5) * 2}s` },
    lifecycle:    { executor: 'constant-vus', vus: Math.ceil(vus / 2), duration: dur, exec: 'fullLifecycle', startTime: `${(parseInt(dur) + 5) * 3}s` },
    search:       { executor: 'constant-vus', vus, duration: dur, exec: 'search', startTime: `${(parseInt(dur) + 5) * 4}s` },
    spike:        { executor: 'ramping-vus', stages: [{ duration: '5s', target: vus * 4 }, { duration: '10s', target: vus * 4 }, { duration: '5s', target: 0 }],
                    exec: 'readPlans', startTime: `${(parseInt(dur) + 5) * 5}s` },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:read_plans}': ['p(95)<100'],
    'http_req_duration{scenario:read_cached}': ['p(95)<100'],
    'http_req_duration{scenario:create}': ['p(95)<200'],
    'http_req_duration{scenario:search}': ['p(95)<200'],
    checks: ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const body = (n) => JSON.stringify({ serviceName: `Load-${n}`, price: 9900 + (n % 100), startDate: '2026-09-05', trialEndsAt: '2026-09-12' });

export function setup() {
  const r = http.post(`${BASE}/subscriptions`, body(0), JSON_HDR);
  if (r.status !== 201) throw new Error(`setup failed: ${r.status} ${r.body}`);
  return { id: r.json('id') };
}

export function readPlans() {
  check(http.get(`${BASE}/plans`), { 'plans 200': (r) => r.status === 200 });
}
export function readSubscription(d) {
  check(http.get(`${BASE}/subscriptions/${d.id}`), { 'get 200': (r) => r.status === 200 });
}
export function createSubscription() {
  check(http.post(`${BASE}/subscriptions`, body(__VU * 1000 + __ITER), JSON_HDR), { 'create 201': (r) => r.status === 201 });
}
export function fullLifecycle() {
  const t0 = Date.now();
  const c = http.post(`${BASE}/subscriptions`, body(__VU * 1000 + __ITER), JSON_HDR);
  if (!check(c, { 'create 201': (r) => r.status === 201 })) return;
  const id = c.json('id');
  check(http.post(`${BASE}/subscriptions/${id}/pause`), { 'pause 200': (r) => r.status === 200 });
  check(http.post(`${BASE}/subscriptions/${id}/resume`), { 'resume 200': (r) => r.status === 200 });
  const rn = http.post(`${BASE}/subscriptions/${id}/renew`);
  check(rn, { 'renew 200': (r) => r.status === 200 });
  if (rn.status === 200) check(http.post(`${BASE}/invoices/${rn.json('invoice.id')}/pay`), { 'pay 200': (r) => r.status === 200 });
  check(http.del(`${BASE}/subscriptions/${id}`), { 'cancel 204': (r) => r.status === 204 });
  lifecycle.add(Date.now() - t0);
}
export function search() {
  check(http.post(`${BASE}/subscriptions/search`, JSON.stringify({ status: 'ACTIVE', limit: 20 }), JSON_HDR), { 'search 200': (r) => r.status === 200 });
}
