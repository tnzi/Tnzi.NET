/**
 * `TWidgetOpsSnapshot` fills its module-gated rows once the module signal
 * settles.
 *
 * The login-security and channels probes are skipped while the availability
 * probe is in flight (`canActivate` is false then). The widget loaded once on
 * mount, so a dashboard opened right after sign-in kept those rows at "-"
 * until a manual refresh, although the module was loaded.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { onMounted } from 'vue'

const getOverview = vi.fn()

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))
vi.mock('../../src/services/bridges/diagnostics-bridge', () => ({
  createDiagnosticsBridge: () => ({ exceptions: { getSummary: () => Promise.resolve({ totalCount: 0 }) } }),
}))
vi.mock('../../src/services/bridges/performance-bridge', () => ({
  createPerformanceBridge: () => ({ getSummary: () => Promise.resolve({ p95: 12 }) }),
}))
vi.mock('../../src/services/bridges/signalr-bridge', () => ({
  createSignalRBridge: () => ({ getStats: () => Promise.resolve({ onlineUserCount: 1 }) }),
}))
vi.mock('../../src/services/bridges/channels-bridge', () => ({
  createChannelsBridge: () => ({ channels: { getStatus: () => Promise.resolve({ enabled: true, adapters: [] }) } }),
}))
vi.mock('../../src/services/bridges/login-security-bridge', () => ({
  createLoginSecurityBridge: () => ({ getOverview }),
}))
vi.mock('../../src/headless/useWidgetData', () => ({
  useWidgetData: (loader: () => Promise<void>) => {
    onMounted(() => { void loader() })
    return { reload: loader }
  },
}))

import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import TWidgetOpsSnapshot from '../../src/components/widgets/TWidgetOpsSnapshot.vue'

describe('TWidgetOpsSnapshot module signal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getOverview.mockReset().mockResolvedValue({ failureRate: 12.5, lockedOutUsers: 0 })
  })

  it('probes login security once the module signal settles after mount', async () => {
    const routes = useAdminRouteStore()
    routes.setModuleSignalPending(true)
    const wrapper = mount(TWidgetOpsSnapshot, { global: { stubs: { TSvgIcon: true } } })
    await flushPromises()
    expect(getOverview).not.toHaveBeenCalled()

    routes.setAvailableModules(new Set(['identity']))
    routes.setModuleSignalPending(false)
    await flushPromises()

    expect(getOverview).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('12.5%')
  })
})
