/**
 * Dashboard widgets tell "no figure" apart from "zero".
 *
 * A metric whose load failed (timeout, 500, network, a refused read) rendered
 * as the number 0, which reads as data ("no users", "no failed messages")
 * rather than as an unavailable figure. A failed figure now shows the empty
 * placeholder; a real zero still shows 0.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { onMounted } from 'vue'

const usersFetch = vi.fn()
const accessStats = vi.fn()
const aiSummary = vi.fn()
const ordersFetch = vi.fn()
const messagesFetch = vi.fn()

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))
vi.mock('../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({ users: { fetch: usersFetch } }),
}))
vi.mock('../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({ accessLogs: { statistics: accessStats } }),
}))
vi.mock('../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({ usage: { summary: aiSummary } }),
}))
vi.mock('../../src/services/bridges/payment-bridge', () => ({
  createPaymentBridge: () => ({ orders: { fetch: ordersFetch } }),
}))
vi.mock('../../src/services/bridges/notification-bridge', () => ({
  createNotificationBridge: () => ({ messages: { fetch: messagesFetch } }),
}))
vi.mock('../../src/headless/useWidgetData', () => ({
  useWidgetData: (loader: () => Promise<void>) => {
    onMounted(() => { void loader() })
    return { reload: loader }
  },
}))

import TWidgetKpiStrip from '../../src/components/widgets/TWidgetKpiStrip.vue'
import TWidgetNotificationStats from '../../src/components/widgets/TWidgetNotificationStats.vue'

async function mountStrip() {
  const wrapper = mount(TWidgetKpiStrip, {
    global: {
      stubs: {
        TCountTo: { props: ['endValue'], template: '<span class="count">{{ endValue }}</span>' },
        TSvgIcon: true,
      },
    },
  })
  await flushPromises()
  return wrapper
}

function tileValues(wrapper: Awaited<ReturnType<typeof mountStrip>>): string[] {
  return wrapper.findAll('.t-widget-kpi__value').map((n) => n.text())
}

describe('TWidgetKpiStrip failed loads', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    usersFetch.mockReset().mockRejectedValue(new Error('timeout'))
    accessStats.mockReset().mockResolvedValue({ captureEnabled: true, totalRequests: 0 })
    aiSummary.mockReset().mockRejectedValue(new Error('500'))
    ordersFetch.mockReset().mockResolvedValue({ items: [], totalCount: 7 })
  })

  it('shows the empty placeholder for a failed metric and 0 only for a real zero', async () => {
    const wrapper = await mountStrip()
    expect(tileValues(wrapper)).toEqual(['-', '0', '-', '7'])
  })
})

describe('TWidgetNotificationStats failed loads', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    messagesFetch.mockReset()
  })

  it('shows the empty placeholder for a count it could not read, and 0 for a real zero', async () => {
    messagesFetch
      .mockResolvedValueOnce({ items: [], totalCount: 0 })
      .mockRejectedValueOnce(new Error('timeout'))
    const wrapper = mount(TWidgetNotificationStats, { global: { stubs: { TSvgIcon: true } } })
    await flushPromises()
    expect(wrapper.findAll('.t-widget-notif__value').map((n) => n.text())).toEqual(['0', '-'])
  })
})
