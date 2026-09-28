import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 10,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<2000'],
  },
};

const baseUrl = __ENV.BASE_URL || 'http://localhost:5053';

export default function () {
  const payload = JSON.stringify({
    name: `k6-${__VU}-${__ITER}`,
    tasks: [
      { key: 'extract', type: 'echo', payload: { step: 'extract' }, dependsOn: [] },
      { key: 'load', type: 'echo', payload: { step: 'load' }, dependsOn: ['extract'] },
    ],
  });

  const res = http.post(`${baseUrl}/api/jobs`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'accepted': (r) => r.status === 201,
  });

  if (res.status === 201) {
    const id = res.json('id');
    http.get(`${baseUrl}/api/jobs/${id}`);
  }

  sleep(0.2);
}
