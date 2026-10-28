import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// The strip reads its figures off the aging report, which needs
// finance.report.view while the hosting list pages need only document access.
// A failed or forbidden read left Overdue at 0.00 in the success tone: a green
// "nothing overdue" stated about books the strip never saw.

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/', fullPath: '/', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

import DocumentKpiStrip from '../../../src/pages/finance/components/DocumentKpiStrip.vue'

const arAging = vi.fn()
const apAging = vi.fn()
const bridge = { reports: { arAging, apAging } }

function mountStrip(kind: 'ar' | 'ap' = 'ar') {
  return mount(DocumentKpiStrip, {
    props: { bridge: bridge as never, kind, translate: (k: string) => k },
  })
}

function overdueCard(wrapper: ReturnType<typeof mountStrip>) {
  const card = wrapper.findAll('.t-stat-card').find((c) => c.text().includes('kpi.overdue'))
  expect(card, 'overdue card').toBeDefined()
  return card!
}

describe('DocumentKpiStrip', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    arAging.mockReset()
    apAging.mockReset()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('★ a failed aging read shows no figure and no "all clear" tone', async () => {
    arAging.mockRejectedValue(new Error('Request failed with status code 403'))
    const wrapper = mountStrip()
    await flushPromises()

    const card = overdueCard(wrapper)
    expect(card.text()).not.toContain('0.00')
    expect(card.find('.t-stat-card__value--success').exists()).toBe(false)
  })

  it('shows the overdue figure in the error tone when something is overdue', async () => {
    arAging.mockResolvedValue({ totals: { total: 150, current: 100 }, rows: [{}, {}], baseCurrency: 'CAD' })
    const wrapper = mountStrip()
    await flushPromises()

    const card = overdueCard(wrapper)
    expect(card.text()).toContain('50.00')
    expect(card.find('.t-stat-card__value--error').exists()).toBe(true)
  })

  it('★ asks for the aging as of the local calendar date, not the UTC one', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    // Late evening local time: for any viewer west of Greenwich the UTC date
    // is already tomorrow. (In UTC itself the two dates agree.)
    vi.setSystemTime(new Date(2026, 9, 10, 23, 30))
    apAging.mockResolvedValue({ totals: { total: 0, current: 0 }, rows: [], baseCurrency: 'CAD' })
    mountStrip('ap')
    await flushPromises()

    expect(apAging).toHaveBeenCalledWith('2026-10-10')
  })
})
