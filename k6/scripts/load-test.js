import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const successRate = new Rate('success_rate');
const requestDuration = new Trend('request_duration_ms');
const errorCount = new Counter('errors');

export const options = {
  scenarios: {
    constant_load: {
      executor: 'constant-arrival-rate',
      rate: __ENV.RPS ? parseInt(__ENV.RPS) : 10,
      timeUnit: '1s',
      duration: __ENV.DURATION || '60s',
      preAllocatedVUs: __ENV.VUS ? parseInt(__ENV.VUS) : 50,
      maxVUs: __ENV.MAX_VUS ? parseInt(__ENV.MAX_VUS) : 100,
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<500'],
    success_rate: ['rate>0.95'],
  },
};

export default function () {
  const targetUrl = __ENV.TARGET_URL;
  const payload = __ENV.PAYLOAD || JSON.stringify({ message: 'Hello from k6!' });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
    timeout: '30s',
  };

  const startTime = Date.now();
  const res = http.post(targetUrl, payload, params);
  const duration = Date.now() - startTime;

  requestDuration.add(duration);

  const isSuccess = check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });

  successRate.add(isSuccess);

  if (!isSuccess) {
    errorCount.add(1);
    console.log(`Error: status=${res.status}, body=${res.body}`);
  }
}

export function handleSummary(data) {
  return {
    stdout: JSON.stringify(data, null, 2),
  };
}
