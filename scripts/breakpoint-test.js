// Breakpoint test: the request rate climbs steadily until the service stops keeping up, then the run ABORTS. The rate
// reached at that moment is the capacity of this deployment (read it from "Throughput vs VUs" in Grafana or from
// http_reqs in the summary). An aborted run (exit code 99) is therefore the expected outcome, not a defect.
//   BP_START_RATE (50)  →  BP_MAX_RATE (3000) iterations/s over BP_RAMP (5m)
//   BP_VUS (500) pre-allocated, BP_MAX_VUS (2000) upper bound of concurrent in-flight iterations
// Aborts when >1% of requests fail, p95 > 1 s, or the generator runs out of VUs (too many dropped iterations).
import { env, envNumber, mixedWorkload, seedSubscriptions } from './lib/api.js';

const abort = (threshold) => ({ threshold, abortOnFail: true, delayAbortEval: '15s' });

export const options = {
  scenarios: {
    breakpoint: {
      executor: 'ramping-arrival-rate',
      startRate: envNumber('BP_START_RATE', 50),
      timeUnit: '1s',
      stages: [{ target: envNumber('BP_MAX_RATE', 3000), duration: env('BP_RAMP', '5m') }],
      preAllocatedVUs: envNumber('BP_VUS', 500),
      maxVUs: envNumber('BP_MAX_VUS', 2000),
    },
  },
  thresholds: {
    http_req_failed: [abort('rate<0.01')],
    http_req_duration: [abort('p(95)<1000')],
    dropped_iterations: [abort(`count<${envNumber('BP_MAX_DROPPED', 500)}`)],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { ids: seedSubscriptions(100, 'Breakpoint-seed') };
}

export default function (data) {
  mixedWorkload(data);
}
