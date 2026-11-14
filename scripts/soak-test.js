// Soak (endurance) test: a moderate, constant request rate for a long time. It is not about peak throughput but about
// what only shows up over time: memory growth, GC pauses, connection-pool or Redis leaks, slowly degrading latency.
// Watch the ".NET runtime", "Npgsql pool" and "Redis" rows in Grafana while it runs.
//   SOAK_RATE (100)      iterations per second (open model: the rate does not drop when the service slows down)
//   SOAK_DURATION (10m)  use 1h+ for a real soak
//   SOAK_VUS (100)       pre-allocated VUs (grows up to 4x when responses get slow)
// Passes when <0.5% of requests fail, p95 < 300 ms, p99 < 1 s and under 1% of the planned iterations are dropped.
import { env, envNumber, mixedWorkload, seconds, seedSubscriptions } from './lib/api.js';

const rate = envNumber('SOAK_RATE', 100);
const duration = env('SOAK_DURATION', '10m');
const vus = envNumber('SOAK_VUS', 100);

export const options = {
  scenarios: {
    soak: { executor: 'constant-arrival-rate', rate, timeUnit: '1s', duration, preAllocatedVUs: vus, maxVUs: vus * 4 },
  },
  thresholds: {
    http_req_failed: ['rate<0.005'],
    checks: ['rate>0.995'],
    http_req_duration: ['p(95)<300', 'p(99)<1000'],
    dropped_iterations: [`count<${Math.max(1, Math.ceil(rate * seconds(duration) * 0.01))}`],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  return { ids: seedSubscriptions(100, 'Soak-seed') };
}

export default function (data) {
  mixedWorkload(data);
}
