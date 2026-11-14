// Ramp-up test: concurrent users grow linearly from a small base to the peak, stay there, then leave. Answers: how do
// latency and throughput change as load builds up gradually, and at which VU count do they start to degrade? (Read it
// from "Throughput vs VUs" in Grafana.) Unlike breakpoint it never aborts: the whole ramp is always measured.
//   RAMPUP_START_VUS (50)  →  RAMPUP_VUS (2000) over RAMPUP_DURATION (5m)
//   RAMPUP_HOLD (1m)       time at the peak                RAMPUP_DOWN (30s)   ramp back to 0
// Passes when <1% of requests fail and p95 < 1 s over the whole run.
import { env, envNumber, mixedWorkload, seedSubscriptions } from './lib/api.js';

const peak = envNumber('RAMPUP_VUS', 2000);

export const options = {
  scenarios: {
    rampup: {
      executor: 'ramping-vus',
      startVUs: envNumber('RAMPUP_START_VUS', 50),
      stages: [
        { duration: env('RAMPUP_DURATION', '5m'), target: peak },
        { duration: env('RAMPUP_HOLD', '1m'), target: peak },
        { duration: env('RAMPUP_DOWN', '30s'), target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    http_req_duration: ['p(95)<1000'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { ids: seedSubscriptions(100, 'Rampup-seed') };
}

export default function (data) {
  mixedWorkload(data);
}
