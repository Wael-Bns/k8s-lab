import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, defaultThresholds } from './config.js';

export const options = {
  scenarios: {
    weather_traffic: {
      executor: 'constant-vus',
      vus: 100,
      duration: '3m',
    },
  },
  thresholds: defaultThresholds,
};

export default function () {
  const res = http.get(`${BASE_URL}/api/weather`);
  check(res, {
    'weather status is 200': (r) => r.status === 200,
    'weather returned data': (r) => r.json().length === 5,
  });
  sleep(0.1);
}