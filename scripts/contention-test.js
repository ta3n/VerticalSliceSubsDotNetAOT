// Contention test: correctness under concurrency, not speed. Many clients race the SAME operation on the SAME row and
// exactly one may win (the API serialises them with row locks / unique keys):
//   pay one invoice      → one 200, the rest 409 invoice-not-payable
//   cancel one subscription → one 204, the rest 400 subscription-already-canceled
//   create one plan code → one 201, the rest 409 plan-already-exists
// while other VUs do the same on other rows.
//   CONTENTION_VUS (10) · CONTENTION_DURATION (30s) · CONTENTION_PARALLEL (8 racers per operation)
//   CONTENTION_PLAN_RACES (10 in total: plans cannot be deleted, so this stays small)
// Passes when no race ever has zero or several winners (contention_violations = 0) and nothing fails unexpectedly.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { api, env, envNumber, planBody, race, subscriptionBody, unique } from './lib/api.js';

const parallel = envNumber('CONTENTION_PARALLEL', 8);
const violations = new Counter('contention_violations');

export const options = {
  batch: parallel,
  batchPerHost: parallel,
  scenarios: {
    row_races: { executor: 'constant-vus', vus: envNumber('CONTENTION_VUS', 10), duration: env('CONTENTION_DURATION', '30s'), exec: 'rowRaces' },
    plan_races: { executor: 'shared-iterations', vus: 2, iterations: envNumber('CONTENTION_PLAN_RACES', 10), maxDuration: '2m', exec: 'planRace' },
  },
  thresholds: {
    contention_violations: ['count<1'],
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
  },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

/** Exactly one response has `winner`, all others have `loser`. Records a violation otherwise. */
function exactlyOne(responses, winner, loser, name) {
  const won = responses.filter((r) => r.status === winner).length;
  const lost = responses.filter((r) => r.status === loser).length;
  const ok = won === 1 && lost === responses.length - 1;
  violations.add(ok ? 0 : 1, { race: name });
  check(null, { [`${name}: exactly one ${winner}, the rest ${loser}`]: () => ok });
  if (!ok) console.error(`${name}: statuses ${responses.map((r) => r.status).join(',')}`);
}

export function rowRaces() {
  const created = api.createSubscription(subscriptionBody(unique('Contention')));
  if (!check(created, { 'setup: create 201': (r) => r.status === 201 })) return;
  const id = created.json('id');
  const renewed = api.renewSubscription(id);
  if (!check(renewed, { 'setup: renew 200': (r) => r.status === 200 })) return;
  const invoiceId = renewed.json('invoice.id');

  exactlyOne(http.batch(race(parallel, 'POST', `/invoices/${invoiceId}/pay`, 'POST /invoices/{id}/pay', [200, 409])), 200, 409, 'pay');
  check(api.getInvoice(invoiceId), { 'pay: invoice ends up PAID': (r) => r.status === 200 && r.json('statusLabel') === 'PAID' });

  exactlyOne(http.batch(race(parallel, 'DELETE', `/subscriptions/${id}`, 'DELETE /subscriptions/{id}', [204, 400])), 204, 400, 'cancel');
  // Not from the cache: the winner evicted the entry, so this read must see the canceled row.
  check(api.getSubscription(id), { 'cancel: subscription ends up Canceled': (r) => r.status === 200 && r.json('status') === 2 });
}

export function planRace() {
  const code = unique('race').toLowerCase();
  exactlyOne(http.batch(race(parallel, 'POST', '/plans', 'POST /plans', [201, 409], planBody(code))), 201, 409, 'plan');
  check(api.getPlan(code), { 'plan: the winner is readable': (r) => r.status === 200 && r.json('code') === code });
}
