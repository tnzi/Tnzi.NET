import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * A failed diagnostics read must not read as an answer. The sensitive-endpoints
 * tab is the one that matters most: its empty state is a green shield and "No
 * sensitive endpoints are active in this deployment", so a 403 or 500 used to
 * show an all-clear on the page whose job is exposure review. The other tabs had
 * the same shape ("no modules", "no exceptions").
 */
const h = vi.hoisted(() => ({
  modules: vi.fn(),
  controllers: vi.fn(),
  sensitive: vi.fn(),
  summary: vi.fn(),
  recent: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/diagnostics-bridge', () => ({
  createDiagnosticsBridge: () => ({
    exceptions: { getSummary: h.summary, getRecent: h.recent, clear: vi.fn() },
    controllers: { list: h.controllers },
    modules: { list: h.modules },
    sensitiveEndpoints: { list: h.sensitive },
  }),
}))

import Diagnostics from '../../../src/pages/system/Diagnostics.vue'

// Every tab rendered at once, and each table showing its #empty slot, so the
// test reads what a viewer of an empty table would read.
const stubs = {
  TTabsPage: {
    template:
      '<div><slot name="kpis" /><section class="tab-modules"><slot name="modules" /></section>' +
      '<section class="tab-controllers"><slot name="controllers" /></section>' +
      '<section class="tab-sensitive"><slot name="sensitive" /></section>' +
      '<section class="tab-exceptions"><slot name="exceptions" /></section></div>',
  },
  TResponsiveTable: { props: ['data'], template: '<div class="tbl"><slot v-if="!data.length" name="empty" /></div>' },
  TEmpty: { props: ['text', 'icon'], template: '<p class="empty" :data-icon="icon">{{ text }}</p>' },
  TKpiCard: { props: ['label', 'value'], template: '<span class="kpi" :data-label="label">{{ value === null ? "-" : value }}</span>' },
  TKpiRow: { template: '<div><slot /></div>' },
}

function tabText(wrapper: ReturnType<typeof mount>, tab: string): string {
  return wrapper.find(`.tab-${tab}`).text()
}

describe('Diagnostics - a failed read says so where the answer would be', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.modules.mockReset().mockResolvedValue([])
    h.controllers.mockReset().mockResolvedValue({ totalCount: 0, controllers: [] })
    h.sensitive.mockReset().mockResolvedValue({ totalCount: 0, endpoints: [] })
    h.summary.mockReset().mockResolvedValue({ totalCount: 0, topExceptions: [], byStatusCode: {}, byErrorCode: {} })
    h.recent.mockReset().mockResolvedValue([])
  })

  it('★ the sensitive tab shows the reason, never the all-clear, when the report cannot be read', async () => {
    h.sensitive.mockRejectedValue(new Error('Forbidden'))
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await flushPromises()

    const text = tabText(wrapper, 'sensitive')
    expect(text).toContain('Forbidden')
    expect(text).not.toContain('No sensitive endpoints are active')
    expect(wrapper.find('.tab-sensitive .empty').attributes('data-icon')).not.toBe('mdi:shield-check-outline')
  })

  it('the sensitive tab keeps its all-clear for a report that has no rows', async () => {
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await flushPromises()

    expect(tabText(wrapper, 'sensitive')).toContain('No sensitive endpoints are active')
  })

  it('the modules, controllers and exceptions tabs show the reason of their own failed read', async () => {
    h.modules.mockRejectedValue(new Error('Modules unavailable'))
    h.controllers.mockRejectedValue(new Error('Controllers unavailable'))
    h.recent.mockRejectedValue(new Error('Exceptions unavailable'))
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await flushPromises()

    expect(tabText(wrapper, 'modules')).toContain('Modules unavailable')
    expect(tabText(wrapper, 'controllers')).toContain('Controllers unavailable')
    expect(tabText(wrapper, 'exceptions')).toContain('Exceptions unavailable')
  })

  it('a failed module read is not counted as zero modules', async () => {
    h.modules.mockRejectedValue(new Error('Modules unavailable'))
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await flushPromises()

    const moduleKpi = wrapper.findAll('.kpi').find((k) => k.attributes('data-label') === 'Loaded Modules')
    expect(moduleKpi?.text()).toBe('-')
  })

  it('a refresh that succeeds clears the previous failure', async () => {
    h.sensitive.mockRejectedValueOnce(new Error('Forbidden'))
    const wrapper = mount(Diagnostics, { global: { stubs } })
    await flushPromises()
    expect(tabText(wrapper, 'sensitive')).toContain('Forbidden')

    await (wrapper.vm as unknown as { refreshSensitive: () => Promise<void> }).refreshSensitive()
    await flushPromises()
    expect(tabText(wrapper, 'sensitive')).not.toContain('Forbidden')
  })
})
