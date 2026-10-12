// Mixed-workload step for the stress ladder (scripts/stress-test.sh). One run = one constant VU level.
import http from 'k6/http';
import { check } from 'k6';
const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const H = { headers: { 'Content-Type': 'application/json' }, timeout: '10s' };
export const options = {
  vus: Number(__ENV.VUS || 100), duration: __ENV.DURATION || '20s',
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};
export function setup() { return { id: http.post(`${BASE}/subscriptions`, JSON.stringify({ serviceName: 'Stress', price: 1, startDate: '2026-09-05' }), H).json('id') }; }
export default function (d) {
  const r = Math.random();   // 40% plans, 30% cached get, 15% search, 15% create
  let res;
  if (r < 0.40) res = http.get(`${BASE}/plans`, H);
  else if (r < 0.70) res = http.get(`${BASE}/subscriptions/${d.id}`, H);
  else if (r < 0.85) res = http.post(`${BASE}/subscriptions/search`, JSON.stringify({ status: 'ACTIVE', limit: 20 }), H);
  else res = http.post(`${BASE}/subscriptions`, JSON.stringify({ serviceName: `S-${__VU}-${__ITER}`, price: 100, startDate: '2026-09-05' }), H);
  check(res, { ok: (x) => x.status === 200 || x.status === 201 });
}
