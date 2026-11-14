// Spike test: a steady background of users plus two sudden bursts. Answers: does the service survive a burst, what
// do the *regular* users feel while it lasts, and does latency return to normal afterwards?
//   SPIKE_BASE_VUS (20)   steady users for the whole run        SPIKE_VUS (400)      burst size
//   SPIKE_RAMP (5s)       how fast a burst arrives / leaves     SPIKE_HOLD (30s)     how long it stays
//   SPIKE_RECOVERY (30s)  quiet time before, between and after the bursts
// Passes when <1% of requests fail and the steady users keep p95 < 500 ms (burst traffic itself: p95 < 1.5 s).
import { envNumber, env, mixedWorkload, seconds, seedSubscriptions } from './lib/api.js';

const baseVus = envNumber('SPIKE_BASE_VUS', 20);
const peak = envNumber('SPIKE_VUS', 400);
const ramp = env('SPIKE_RAMP', '5s');
const hold = env('SPIKE_HOLD', '30s');
const recovery = env('SPIKE_RECOVERY', '30s');

const burst = [{ duration: ramp, target: peak }, { duration: hold, target: peak }, { duration: ramp, target: 0 }];
const stages = [{ duration: recovery, target: 0 }, ...burst, { duration: recovery, target: 0 }, ...burst, { duration: recovery, target: 0 }];
const total = stages.reduce((sum, stage) => sum + seconds(stage.duration), 0);

export const options = {
  scenarios: {
    steady: { executor: 'constant-vus', vus: baseVus, duration: `${total}s`, exec: 'traffic' },
    spike: { executor: 'ramping-vus', startVUs: 0, stages, gracefulRampDown: '5s', exec: 'traffic' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    'http_req_duration{scenario:steady}': ['p(95)<500'],
    'http_req_duration{scenario:spike}': ['p(95)<1500'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { ids: seedSubscriptions(50, 'Spike-seed') };
}

export function traffic(data) {
  mixedWorkload(data);
}
