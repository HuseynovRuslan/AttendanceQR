// Single-user latency for the employee Home flow — the three requests Home fires on mount.
// Run per seeded employee (30 / 365 / 1000 records) to see if latency grows with history.
//
//   k6 run -e BASE=http://localhost:8080 -e PHONE=+994... -e PIN=1234 performance/k6/single-user.js
//
// 5 warm-up iterations (discarded) then 30 measured. Reports p50/p95/p99 per endpoint + full Home flow.
import http from 'k6/http'
import { check } from 'k6'
import { Trend } from 'k6/metrics'

const BASE = __ENV.BASE || 'http://localhost:8080'
const PHONE = __ENV.PHONE || '+994500000000'
const PIN = __ENV.PIN || '1234'

const tProfile = new Trend('home_profile_ms', true)
const tMe = new Trend('home_me_ms', true)
const tSummary = new Trend('home_summary_ms', true)
const tHomeTotal = new Trend('home_total_ms', true)
const meBytes = new Trend('me_payload_bytes')

export const options = {
  scenarios: {
    warmup: { executor: 'shared-iterations', vus: 1, iterations: 5, exec: 'home', tags: { phase: 'warmup' } },
    measure: { executor: 'shared-iterations', vus: 1, iterations: 30, startTime: '4s', exec: 'home', tags: { phase: 'measure' } },
  },
}

export function setup() {
  const r = http.post(`${BASE}/api/auth/app-login`, JSON.stringify({ email: PHONE, password: PIN }), {
    headers: { 'Content-Type': 'application/json', Origin: 'https://app.qrlog.az' },
  })
  check(r, { 'login 200': (x) => x.status === 200 })
  return { token: r.json('token') }
}

export function home(data) {
  const h = { headers: { Authorization: `Bearer ${data.token}` } }
  const from = new Date().toISOString().slice(0, 7) + '-01'
  const to = new Date().toISOString().slice(0, 10)

  const t0 = Date.now()
  // Home fires these three in parallel (HomePage.tsx:40) — batch them the same way.
  const res = http.batch([
    ['GET', `${BASE}/api/attendance/me/profile`, null, h],
    ['GET', `${BASE}/api/attendance/me`, null, h],
    ['GET', `${BASE}/api/reports/summary?from=${from}&to=${to}`, null, h],
  ])
  const total = Date.now() - t0

  tProfile.add(res[0].timings.duration)
  tMe.add(res[1].timings.duration)
  tSummary.add(res[2].timings.duration)
  tHomeTotal.add(total)
  meBytes.add(res[1].body ? res[1].body.length : 0)

  check(res[1], { '/me 200': (r) => r.status === 200 })
}
