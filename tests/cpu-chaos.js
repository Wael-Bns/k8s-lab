import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL } from './config.js';

export const options = {
  scenarios: {
    cpu_burn: {
      executor: 'per-vu-iterations',
      vus: 1,
      iterations: 1,
    },
  },
};

export default function () {
  console.log('--- Triggering CPU Chaos (20s) ---');
  const res = http.post(`${BASE_URL}/api/chaos/cpu?seconds=20`);
  check(res, { 'cpu chaos accepted': (r) => r.status === 200 });
}