import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL } from './config.js';

export const options = {
  scenarios: {
    unready_toggle: {
      executor: 'per-vu-iterations',
      vus: 1,
      iterations: 1,
    },
  },
};

export default function () {
  console.log('--- Flipping Readiness to FALSE ---');
  http.post(`${BASE_URL}/api/chaos/unready`);

  // Verify /health/ready returns 503 while unready
  const checkUnready = http.get(`${BASE_URL}/health/ready`);
  check(checkUnready, { 'readiness probe fails (503)': (r) => r.status === 503 });

  sleep(5);

  console.log('--- Flipping Readiness back to TRUE ---');
  http.post(`${BASE_URL}/api/chaos/ready`);

  const checkReady = http.get(`${BASE_URL}/health/ready`);
  check(checkReady, { 'readiness probe passes (200)': (r) => r.status === 200 });
}