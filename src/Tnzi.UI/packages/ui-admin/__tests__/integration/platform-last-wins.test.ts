import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Selection-driven panels whose loader re-reads on every click: a slower answer
 * for the previous selection must not land under the current one. Each case
 * holds the reads open per selection, answers the later one first, then the
 * earlier one, and checks the panel still shows the later selection's data.
 */
const h = vi.hoisted(() => ({
  pending: new Map<string, (value: unknown) => void>(),
  getUsers: vi.fn(),
  listSessions: vi.fn(),
  stats: vi.fn(),
  trend: vi.fn(),
}))

/** A read held open under `key` until the test answers it. */
function held(key: string): Promise<unknown> {
  return new Promise((resolve) => { h.pending.set(key, resolve) })
}

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/admin', fullPath: '/admin', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))
vi.mock('../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    organizations: { getTree: vi.fn(async () => []), getUsers: h.getUsers },
    users: { removeFromOrganization: vi.fn() },
    sessions: {
      list: h.listSessions,
      statistics: vi.fn(async () => null),
      activeUsers: vi.fn(async () => []),
    },
  }),
}))
vi.mock('../../src/services/bridges/feature-bridge', () => ({
  createFeatureBridge: () => ({
    usage: {
      mostUsed: vi.fn(async () => []),
      stats: h.stats,
      trend: h.trend,
      cleanup: vi.fn(),
    },
  }),
}))

import Organizations from '../../src/pages/identity/Organizations.vue'
import Sessions from '../../src/pages/identity/Sessions.vue'
import FeatureUsageTab from '../../src/pages/system/features/FeatureUsageTab.vue'

describe('selection-driven panels keep the last selection', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.pending.clear()
  })

  it('★ organization members: unit B stays on screen when unit A answers late', async () => {
    h.getUsers.mockImplementation((orgId: string) => held(orgId))
    const wrapper = mount(Organizations)
    await flushPromises()
    const vm = wrapper.vm as unknown as { selectedKey: string | null; members: { items: Array<{ id: string }> } }

    vm.selectedKey = 'A'
    await flushPromises()
    vm.selectedKey = 'B'
    await flushPromises()

    h.pending.get('B')!({ items: [{ id: 'member-of-B' }], totalCount: 1 })
    await flushPromises()
    h.pending.get('A')!({ items: [{ id: 'member-of-A' }], totalCount: 1 })
    await flushPromises()

    expect(vm.members.items.map((m) => m.id)).toEqual(['member-of-B'])
    wrapper.unmount()
  })

  it("★ sessions: the user picked last stays on screen when the previous user's read answers late", async () => {
    h.listSessions.mockImplementation((q: { userId: string | null }) => held(String(q.userId)))
    const wrapper = mount(Sessions)
    await flushPromises()
    h.pending.get('null')?.({ items: [], totalCount: 0 })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      targetUserId: string | null
      onFilterChanged: () => void
      sessions: Array<{ id: string }>
    }

    vm.targetUserId = 'u1'
    vm.onFilterChanged()
    vm.targetUserId = 'u2'
    vm.onFilterChanged()
    await flushPromises()

    h.pending.get('u2')!({ items: [{ id: 'session-of-u2' }], totalCount: 1 })
    await flushPromises()
    h.pending.get('u1')!({ items: [{ id: 'session-of-u1' }], totalCount: 1 })
    await flushPromises()

    expect(vm.sessions.map((s) => s.id)).toEqual(['session-of-u2'])
    wrapper.unmount()
  })

  it('★ feature usage: feature B stays charted when feature A answers late', async () => {
    h.stats.mockImplementation((name: string) => held(`stats:${name}`))
    h.trend.mockImplementation((name: string) => held(`trend:${name}`))
    const wrapper = mount(FeatureUsageTab, { props: {} })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      select: (name: string) => Promise<void>
      stats: { featureName: string } | null
      trend: Array<{ period: string }>
    }

    void vm.select('A')
    void vm.select('B')
    await flushPromises()

    h.pending.get('stats:B')!({ featureName: 'B' })
    h.pending.get('trend:B')!([{ period: 'b' }])
    await flushPromises()
    h.pending.get('stats:A')!({ featureName: 'A' })
    h.pending.get('trend:A')!([{ period: 'a' }])
    await flushPromises()

    expect(vm.stats?.featureName).toBe('B')
    expect(vm.trend.map((p) => p.period)).toEqual(['b'])
    wrapper.unmount()
  })

  it('★ feature usage: switching the period while a feature loads keeps its stats and charts the new period', async () => {
    // The period select re-reads only the trend; it must not also drop the
    // stats the selection is still waiting for (the KPI row would stay on the
    // previous feature, or blank, under this feature's name).
    h.stats.mockImplementation((name: string) => held(`stats:${name}`))
    h.trend.mockImplementation((name: string, period: string) => held(`trend:${name}:${period}`))
    const wrapper = mount(FeatureUsageTab, { props: {} })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      select: (name: string) => Promise<void>
      loadTrend: () => Promise<void>
      period: string
      stats: { featureName: string } | null
      trend: Array<{ period: string }>
      trendLoading: boolean
    }

    void vm.select('B')
    await flushPromises()
    vm.period = 'weekly'
    void vm.loadTrend()
    await flushPromises()

    h.pending.get('trend:B:weekly')!([{ period: 'weekly' }])
    await flushPromises()
    h.pending.get('stats:B')!({ featureName: 'B' })
    h.pending.get('trend:B:daily')!([{ period: 'daily' }])
    await flushPromises()

    expect(vm.stats?.featureName).toBe('B')
    expect(vm.trend.map((p) => p.period)).toEqual(['weekly'])
    expect(vm.trendLoading).toBe(false)
    wrapper.unmount()
  })

  it('★ feature usage: picking another feature clears the previous one while it loads', async () => {
    h.stats.mockImplementation((name: string) => held(`stats:${name}`))
    h.trend.mockImplementation((name: string) => held(`trend:${name}`))
    const wrapper = mount(FeatureUsageTab, { props: {} })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      select: (name: string) => Promise<void>
      stats: { featureName: string } | null
      trend: Array<{ period: string }>
    }

    void vm.select('A')
    await flushPromises()
    h.pending.get('stats:A')!({ featureName: 'A' })
    h.pending.get('trend:A')!([{ period: 'a' }])
    await flushPromises()
    expect(vm.stats?.featureName).toBe('A')

    void vm.select('B')
    await flushPromises()

    // B's reads are still out: nothing of A may stand under B's name.
    expect(vm.stats).toBeNull()
    expect(vm.trend).toEqual([])
    wrapper.unmount()
  })
})
