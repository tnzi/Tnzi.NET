import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * `/health*` is mapped at the API host's root, outside the API prefix, so the
 * page fetches it directly instead of through the HttpClient. It used to fetch
 * it relative to the admin SPA's own origin: with the API on another origin it
 * probed the SPA host and showed "unavailable", or the SPA's index.html as the
 * overall status.
 */
const h = vi.hoisted(() => ({ baseUrl: '/api' }))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({
    resolveUrl: (url: string) => `${h.baseUrl}${url}`,
    getAccessToken: () => null,
  }),
}))

import HealthChecks from '../../../src/pages/system/HealthChecks.vue'

const healthy = { status: 'Healthy', totalDuration: 3, entries: [{ name: 'db', status: 'Healthy', duration: 2 }] }

function respond(body: string, contentType: string, status = 200): Response {
  return new Response(body, { status, headers: { 'Content-Type': contentType } })
}

describe('HealthChecks page', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    setActivePinia(createPinia())
    fetchMock.mockReset().mockResolvedValue(respond(JSON.stringify(healthy), 'application/json'))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    h.baseUrl = '/api'
  })

  it('probes the API host when the API is on another origin', async () => {
    h.baseUrl = 'https://api.example.com/api'
    mount(HealthChecks)
    await flushPromises()

    expect(fetchMock.mock.calls[0]![0]).toBe('https://api.example.com/health/ready')
  })

  it('keeps a same-origin probe relative', async () => {
    mount(HealthChecks)
    await flushPromises()

    expect(fetchMock.mock.calls[0]![0]).toBe('/health/ready')
  })

  it('reads an HTML page (an SPA fallback or a proxy error page) as unavailable, not as the status', async () => {
    fetchMock.mockResolvedValue(respond('<!doctype html><html><body>app</body></html>', 'text/html; charset=utf-8'))
    const wrapper = mount(HealthChecks)
    await flushPromises()

    const vm = wrapper.vm as unknown as { unavailable: boolean; report: unknown }
    expect(vm.unavailable).toBe(true)
    expect(vm.report).toBeNull()
  })

  it('still shows a plain-text status body', async () => {
    fetchMock.mockResolvedValue(respond('Unhealthy', 'text/plain', 503))
    const wrapper = mount(HealthChecks)
    await flushPromises()

    const vm = wrapper.vm as unknown as { unavailable: boolean; report: { status: string } | null }
    expect(vm.unavailable).toBe(false)
    expect(vm.report?.status).toBe('Unhealthy')
  })
})
