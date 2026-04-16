import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.4 — RunMonitor SSE streaming page test.
 *
 * Lessons applied:
 *   - 1: setActivePinia in beforeEach
 *   - 12: bridge mocked at the boundary
 *   - vue-router useRoute mocked at module level
 *
 * NEW lesson 16: stub global EventSource via vi.stubGlobal so the page can
 * open a stream without a real server. The mock captures URLs, exposes
 * onmessage/onerror handlers, and tracks close() invocations.
 */

interface MockESInstance {
  url: string
  onmessage: ((e: MessageEvent) => void) | null
  onerror: ((e: Event) => void) | null
  close: ReturnType<typeof vi.fn>
}

const esInstances: MockESInstance[] = []

class MockEventSource {
  url: string
  onmessage: ((e: MessageEvent) => void) | null = null
  onerror: ((e: Event) => void) | null = null
  close = vi.fn()
  constructor(url: string) {
    this.url = url
    esInstances.push(this as unknown as MockESInstance)
  }
  addEventListener() {
    /* noop */
  }
}

const fetchRunsMock = vi.fn(async () => ({
  items: [
    {
      id: 'run-aaa-111',
      agentId: 'agent-1',
      status: 'Running',
      executionMode: 0,
      inputSummary: 'hi',
      outputSummary: '',
      totalInputTokens: 0,
      totalOutputTokens: 0,
      durationMs: 1200,
      creationTime: '2026-04-13T10:00:00Z',
    },
    {
      id: 'run-bbb-222',
      agentId: 'agent-1',
      status: 'Succeeded',
      executionMode: 0,
      inputSummary: 'yo',
      outputSummary: 'done',
      totalInputTokens: 5,
      totalOutputTokens: 10,
      durationMs: 5400,
      creationTime: '2026-04-13T09:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 30,
}))

const cancelMock = vi.fn(async () => undefined)

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agentRuns: {
      fetch: fetchRunsMock,
      cancel: cancelMock,
      tail: vi.fn(),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { agentId: 'agent-1' } }),
}))

import RunMonitor from '../../src/pages/ai/agents/RunMonitor.vue'

const stubs = {
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}

describe('RunMonitor page (Phase 5 Task 5.4 SSE streaming)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    esInstances.length = 0
    fetchRunsMock.mockClear()
    cancelMock.mockClear()
    vi.stubGlobal('EventSource', MockEventSource)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('mounts and fetches runs filtered by route agentId', async () => {
    const wrapper = mount(RunMonitor, { global: { stubs } })
    await flushPromises()

    expect(fetchRunsMock).toHaveBeenCalledTimes(1)
    const queryArg = fetchRunsMock.mock.calls[0]?.[0] as { filters: { agentId: string } }
    expect(queryArg.filters.agentId).toBe('agent-1')

    // Two list rows rendered
    const rows = wrapper.findAll('.t-run-monitor__list-row')
    expect(rows).toHaveLength(2)
    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).toContain('Succeeded')
  })

  it('selecting a row opens an EventSource at the conventional stream URL', async () => {
    const wrapper = mount(RunMonitor, { global: { stubs } })
    await flushPromises()

    expect(esInstances).toHaveLength(0)
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    expect(esInstances).toHaveLength(1)
    expect(esInstances[0]?.url).toContain('/api/admin/agent-runs/run-aaa-111/stream')
  })

  it('SSE message updates traceEvents and renders the trace row', async () => {
    const wrapper = mount(RunMonitor, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    const es = esInstances[0]!
    es.onmessage?.(
      new MessageEvent('message', {
        data: JSON.stringify({ type: 'tool_call', content: 'web-search(q="weather")' }),
      }),
    )
    await flushPromises()

    expect(wrapper.text()).toContain('tool_call')
    expect(wrapper.text()).toContain('web-search')
  })

  it('unmount closes the EventSource', async () => {
    const wrapper = mount(RunMonitor, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    const es = esInstances[0]!
    expect(es.close).not.toHaveBeenCalled()
    wrapper.unmount()
    expect(es.close).toHaveBeenCalled()
  })
})
