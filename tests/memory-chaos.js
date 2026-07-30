import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL } from './config.js';

export const options = {
  scenarios: {
    memory_leak: {
      executor: 'per-vu-iterations',
      vus: 1,
      iterations: 1,
    },
  },
};

export default function () {
  console.log('--- Allocating 100MB Leaked Memory ---');
  let res1 = http.post(`${BASE_URL}/api/chaos/memory?megabytes=100`);
  check(res1, { 'memory allocated 100MB': (r) => r.status === 200 });

  sleep(3);

  console.log('--- Resetting Memory ---');
  let res2 = http.post(`${BASE_URL}/api/chaos/memory/reset`);
  check(res2, { 'memory reset': (r) => r.status === 200 });
}