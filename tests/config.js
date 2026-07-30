export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export const defaultThresholds = {
  http_req_failed: ['rate<0.05'], // Max 5% errors
  http_req_duration: ['p(95)<1000'], // P95 latency under 1s
};