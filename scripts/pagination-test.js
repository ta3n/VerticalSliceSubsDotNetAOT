// Pagination test for POST /subscriptions/search (keyset paging on start_date DESC, id DESC). Each iteration walks
// several pages by following nextCursor while other VUs keep inserting rows, and verifies what keyset paging promises:
// no row is returned twice, the order never breaks, and a deep page costs the same as the first one.
//   PAGINATION_VUS (20) · PAGINATION_DURATION (30s) · PAGINATION_PAGES (10 per walk) · PAGINATION_PAGE_SIZE (25)
//   PAGINATION_SEED (300 rows spread over 60 start dates, created in setup) · PAGINATION_WRITERS (2 inserting VUs)
// Passes when no walk sees a duplicate or out-of-order row (pagination_violations = 0) and both the first and the
// deep pages keep p95 < 200 ms.
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { api, env, envNumber, expectProblem, expectStatus, seedSubscriptions, subscriptionBody, unique } from './lib/api.js';

const pages = envNumber('PAGINATION_PAGES', 10);
const pageSize = envNumber('PAGINATION_PAGE_SIZE', 25);
const duration = env('PAGINATION_DURATION', '30s');

const violations = new Counter('pagination_violations');
const firstPage = new Trend('search_first_page_duration', true);
const deepPage = new Trend('search_deep_page_duration', true);

export const options = {
  scenarios: {
    readers: { executor: 'constant-vus', vus: envNumber('PAGINATION_VUS', 20), duration, exec: 'walk' },
    writers: { executor: 'constant-vus', vus: envNumber('PAGINATION_WRITERS', 2), duration, exec: 'insert' },
  },
  thresholds: {
    pagination_violations: ['count<1'],
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    search_first_page_duration: ['p(95)<200'],
    search_deep_page_duration: ['p(95)<200'],
  },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

/** 2026-09-01 + n days, as yyyy-MM-dd. */
function startDate(n) {
  return new Date(Date.UTC(2026, 8, 1 + n)).toISOString().slice(0, 10);
}

export function setup() {
  // Many rows per start date (ties broken by id) and many dates: both halves of the keyset are exercised.
  seedSubscriptions(envNumber('PAGINATION_SEED', 300), 'Page-seed',
    (i) => subscriptionBody(`Page-seed-${i}`, { startDate: startDate(i % 60), trialEndsAt: null }));
}

/** true when `b` sorts after `a` in (startDate DESC, id DESC) order. */
function inOrder(a, b) {
  return a.startDate > b.startDate || (a.startDate === b.startDate && a.id > b.id);
}

export function walk() {
  const status = Math.random() < 0.5 ? 'ACTIVE' : null;
  const seen = new Set();
  let cursor = null;
  let previous = null;
  let broken = 0;

  for (let page = 0; page < pages; page++) {
    const res = api.searchSubscriptions({ status, limit: pageSize, cursor });
    if (!expectStatus(res, 200, 'search 200')) return;
    (page === 0 ? firstPage : deepPage).add(res.timings.duration);

    const body = res.json();
    if (body.items.length > pageSize) broken++;
    for (const item of body.items) {
      if (seen.has(item.id)) broken++;
      if (previous && !inOrder(previous, item)) broken++;
      if (status && item.status !== 1) broken++;
      seen.add(item.id);
      previous = item;
    }

    cursor = body.nextCursor;
    if (!cursor) break;
  }

  violations.add(broken);
  check(null, { 'walk: no duplicate, out-of-order or unfiltered row': () => broken === 0 });

  // Client mistakes on the same endpoint stay cheap and well-formed; an out-of-range limit falls back to 20.
  const roll = Math.random();
  if (roll < 0.05) expectProblem(api.searchSubscriptions({ cursor: 'not-a-cursor' }, [400]), 400, 'invalid-cursor');
  else if (roll < 0.1) expectProblem(api.searchSubscriptions({ status: 'active' }, [400]), 400, 'invalid-subscription-status');
  else if (roll < 0.15) check(api.searchSubscriptions({ limit: 100000 }), { 'search: oversized limit falls back to 20': (r) => r.status === 200 && r.json('items').length <= 20 });
}

export function insert() {
  expectStatus(api.createSubscription(subscriptionBody(unique('Page-live'), { startDate: startDate(Math.floor(Math.random() * 60)), trialEndsAt: null })), 201, 'insert 201');
}
