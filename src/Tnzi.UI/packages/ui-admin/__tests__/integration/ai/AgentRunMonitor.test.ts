import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * AgentRunMonitor - run trace viewer test.
 *
 * The page no longer uses a (non-existent) SSE tail endpoint: it fetches the
 * recorded traces through `bridge.agentRuns.getTraces(runId)` and, while the
 * selected run is non-terminal, re-polls every 3s (stops on a terminal
 * transition and on unmount). The bridge is mocked at the boundary; vue-router
 * useRoute/useRouter are mocked at module level.
 */

const fetchRunsMock = vi.fn(async () => ({
  items: [
    {
      id: 'run-aaa-111',
      agentId: 'agent-1',
      status: 'Running',
      executionMode: 'Single',
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
      status: 'Completed',
      executionMode: 'Single',
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

const getTracesMock = vi.fn(async (runId: string) => [
  {
    id: 'trace-1',
    runId,
    nodeId: null,
    eventType: 'tool_call',
    eventData: 'web-search(q="weather")',
    durationMs: 12,
    creationTime: '2026-04-13T10:00:01Z',
  },
])

const cancelMock = vi.fn(async () => undefined)

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    agentRuns: {
      fetch: fetchRunsMock,
      cancel: cancelMock,
      getTraces: getTracesMock,
      tail: vi.fn(),
    },
  }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { agentId: 'agent-1' }, meta: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
}))

import AgentRunMonitor from '../../../src/pages/ai/agents/AgentRunMonitor.vue'

const stubs = {
  TSvgIcon: { name: 'TSvgIcon', props: ['icon', 'size'], template: '<span />' },
}

describe('AgentRunMonitor page (trace viewer + polling)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    fetchRunsMock.mockClear()
    getTracesMock.mockClear()
    cancelMock.mockClear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('mounts and fetches runs filtered by route agentId', async () => {
    const wrapper = mount(AgentRunMonitor, { global: { stubs } })
    await flushPromises()

    expect(fetchRunsMock).toHaveBeenCalledTimes(1)
    const queryArg = fetchRunsMock.mock.calls[0]?.[0] as { filters: { agentId: string } }
    expect(queryArg.filters.agentId).toBe('agent-1')

    // Two list rows rendered with their (humanised) status labels.
    const rows = wrapper.findAll('.t-run-monitor__list-row')
    expect(rows).toHaveLength(2)
    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).toContain('Completed')
  })

  it('selecting a row fetches that run\'s traces and renders them', async () => {
    const wrapper = mount(AgentRunMonitor, { global: { stubs } })
    await flushPromises()

    expect(getTracesMock).not.toHaveBeenCalled()
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    expect(getTracesMock).toHaveBeenCalledWith('run-aaa-111')
    expect(wrapper.text()).toContain('tool_call')
    expect(wrapper.text()).toContain('web-search')
  })

  it('polls traces every 3s while the selected run is non-terminal', async () => {
    vi.useFakeTimers()
    const wrapper = mount(AgentRunMonitor, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    const initial = getTracesMock.mock.calls.length
    expect(initial).toBeGreaterThan(0)

    await vi.advanceTimersByTimeAsync(3000)
    expect(getTracesMock.mock.calls.length).toBeGreaterThan(initial)

    // Unmount halts the poll loop.
    wrapper.unmount()
    const afterUnmount = getTracesMock.mock.calls.length
    await vi.advanceTimersByTimeAsync(6000)
    expect(getTracesMock.mock.calls.length).toBe(afterUnmount)
  })

  it('does not poll a terminal run', async () => {
    vi.useFakeTimers()
    const wrapper = mount(AgentRunMonitor, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-run-id="run-bbb-222"]').trigger('click')
    await flushPromises()

    const initial = getTracesMock.mock.calls.length
    expect(initial).toBe(1)
    await vi.advanceTimersByTimeAsync(6000)
    expect(getTracesMock.mock.calls.length).toBe(1)
    wrapper.unmount()
  })

  it('cancels the selected run and refreshes', async () => {
    const wrapper = mount(AgentRunMonitor, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-run-id="run-aaa-111"]').trigger('click')
    await flushPromises()

    const cancelBtn = wrapper.find('.t-run-monitor__cancel')
    expect(cancelBtn.exists()).toBe(true)
    await cancelBtn.trigger('click')
    await flushPromises()

    expect(cancelMock).toHaveBeenCalledWith('run-aaa-111')
  })
})
