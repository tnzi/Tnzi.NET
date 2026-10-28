import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import {
  UsageLimitMode,
  UsageLimitWindow,
  type SaveUsagePolicyDto,
  type UsageLimitDefaultsDto,
  type UserUsageLimitsDto,
} from '@tnzi/core/services/ai'

/**
 * The two hostable usage-limit panels.
 *
 * What they must get right: "follow" is never a blank (each item says what it
 * currently resolves to), an amount goes out only on a limited item, a save the
 * server would refuse is stopped before the round trip, and nothing is
 * writable without `ai.quota.update`.
 */
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))

import TUsageLimitDefaultsPanel from '../../../src/components/ai/TUsageLimitDefaultsPanel.vue'
import TUserUsageLimitsPanel from '../../../src/components/ai/TUserUsageLimitsPanel.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const catalog = [
  { id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'claude' },
  { id: 'claude-sonnet-5-5', displayName: 'Sonnet 5.5', provider: 'claude' },
]

const inherit = { mode: UsageLimitMode.Inherit, amountUsd: null }

function defaults(overrides: Partial<UsageLimitDefaultsDto> = {}): UsageLimitDefaultsDto {
  return {
    policy: { sessionLimit: inherit, weeklyLimit: inherit, allowedModels: null, defaultModel: null },
    configuration: {
      sessionLimitUsd: 5,
      weeklyLimitUsd: null,
      allowedModels: [],
      defaultModel: null,
      sessionWindowMinutes: 300,
      weeklyWindowMinutes: 10080,
      warningThreshold: 0.8,
    },
    effective: { sessionLimitUsd: 5, weeklyLimitUsd: null, allowedModels: catalog, defaultModel: null },
    catalog,
    ...overrides,
  }
}

function userLimits(overrides: Partial<UserUsageLimitsDto> = {}): UserUsageLimitsDto {
  return {
    userId: 'u1',
    hasCustomPolicy: false,
    policy: { sessionLimit: inherit, weeklyLimit: inherit, allowedModels: null, defaultModel: null },
    defaults: { sessionLimitUsd: 10, weeklyLimitUsd: 50, allowedModels: catalog, defaultModel: 'claude-sonnet-5-5' },
    effective: { sessionLimitUsd: 10, weeklyLimitUsd: 50, allowedModels: catalog, defaultModel: 'claude-sonnet-5-5' },
    catalog,
    usage: {
      windows: [
        {
          window: UsageLimitWindow.Session,
          durationMinutes: 300,
          limitUsd: 10,
          usedUsd: 4.2,
          usedPercent: 42,
          startedAt: new Date(Date.now() - 3600_000).toISOString(),
          resetsAt: new Date(Date.now() + (2 * 60 + 12) * 60_000 + 20_000).toISOString(),
          exceeded: false,
          warningReached: false,
        },
        {
          window: UsageLimitWindow.Weekly,
          durationMinutes: 10080,
          limitUsd: 50,
          usedUsd: 50.4,
          usedPercent: 100.8,
          startedAt: new Date(Date.now() - 86_400_000).toISOString(),
          resetsAt: new Date(Date.now() + 3 * 86_400_000).toISOString(),
          exceeded: true,
          warningReached: true,
        },
      ],
      warningThreshold: 0.8,
      checkedAt: new Date().toISOString(),
      lastUsedAt: new Date().toISOString(),
    },
    ...overrides,
  }
}

function makeBridge() {
  return {
    getDefaults: vi.fn(async () => defaults()),
    saveDefaults: vi.fn(async (_d: SaveUsagePolicyDto) => defaults()),
    getUser: vi.fn(async (_id: string) => userLimits()),
    saveUser: vi.fn(async (_id: string, _d: SaveUsagePolicyDto) => userLimits({ hasCustomPolicy: true })),
    clearUser: vi.fn(async () => undefined),
    resetUser: vi.fn(async () => undefined),
    fetchUsers: vi.fn(),
    getBudgetSummary: vi.fn(),
  }
}

const formOf = (w: VueWrapper) => w.findComponent({ name: 'TUsageLimitPolicyForm' })
const buttonByText = (w: VueWrapper, text: string) => w.findAll('button').find((b) => b.text().trim() === text)

describe('TUsageLimitDefaultsPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions: ['ai.quota.view', 'ai.quota.update'] })
  })

  it('says what "follow configuration" currently resolves to, and the windows', async () => {
    const bridge = makeBridge()
    const w = mount(TUsageLimitDefaultsPanel, { props: { bridge: bridge as never } })
    await flushPromises()

    const text = w.text()
    expect(text).toContain('Session window: 5 hours. Weekly window: 7 days. Users are warned at 80% used.')
    expect(text).toContain('Currently $5.00')
    expect(text).toContain('Currently Unlimited')
    expect(text).toContain('Session $5.00 · weekly Unlimited · models: Opus 5.5, Sonnet 5.5')
  })

  it('a limited item without an amount is stopped before the round trip', async () => {
    const bridge = makeBridge()
    const w = mount(TUsageLimitDefaultsPanel, { props: { bridge: bridge as never } })
    await flushPromises()

    const draft = formOf(w).props('modelValue') as SaveUsagePolicyDto
    formOf(w).vm.$emit('update:modelValue', { ...draft, sessionLimit: { mode: UsageLimitMode.Limited, amountUsd: null } })
    await flushPromises()

    expect(w.text()).toContain('Enter an amount (0 or more) for every limit set to "Limit".')
    expect(buttonByText(w, 'Save')!.attributes('disabled')).toBeDefined()
  })

  it('saves an amount only on the limited item', async () => {
    const bridge = makeBridge()
    const w = mount(TUsageLimitDefaultsPanel, { props: { bridge: bridge as never } })
    await flushPromises()

    const draft = formOf(w).props('modelValue') as SaveUsagePolicyDto
    formOf(w).vm.$emit('update:modelValue', {
      ...draft,
      sessionLimit: { mode: UsageLimitMode.Limited, amountUsd: 12.5 },
      weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: 99 },
      note: '  pilot group  ',
    })
    await flushPromises()
    await buttonByText(w, 'Save')!.trigger('click')
    await flushPromises()

    expect(bridge.saveDefaults).toHaveBeenCalledWith({
      sessionLimit: { mode: UsageLimitMode.Limited, amountUsd: 12.5 },
      weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: null },
      allowedModels: null,
      defaultModel: null,
      note: 'pilot group',
    })
  })

  it('an empty chosen model list is refused before the round trip', async () => {
    const bridge = makeBridge()
    const w = mount(TUsageLimitDefaultsPanel, { props: { bridge: bridge as never } })
    await flushPromises()

    const draft = formOf(w).props('modelValue') as SaveUsagePolicyDto
    formOf(w).vm.$emit('update:modelValue', { ...draft, allowedModels: [] })
    await flushPromises()

    expect(w.text()).toContain('Choose at least one model, or follow the level below.')
  })

  it('without ai.quota.update nothing is writable', async () => {
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions: ['ai.quota.view'] })
    const bridge = makeBridge()
    const w = mount(TUsageLimitDefaultsPanel, { props: { bridge: bridge as never } })
    await flushPromises()

    expect(buttonByText(w, 'Save')).toBeUndefined()
    expect(formOf(w).props('readonly')).toBe(true)
  })
})

describe('TUserUsageLimitsPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions: ['ai.quota.view', 'ai.quota.update'] })
  })

  it('shows each window: spent of the limit, the reset, and a window at its limit', async () => {
    const bridge = makeBridge()
    const w = mount(TUserUsageLimitsPanel, { props: { userId: 'u1', bridge: bridge as never } })
    await flushPromises()

    const text = w.text()
    expect(text).toContain('$4.20 of $10.00 (42%)')
    expect(text).toContain('Resets in 2 h 13 min')
    expect(text).toContain('$50.40 of $50.00 (101%)')
    expect(text).toContain('At limit')
    expect(text).toContain('Follows defaults')
    // "Follow defaults" says what the defaults are for this user.
    expect(text).toContain('Currently $10.00')
    expect(text).toContain('Currently $50.00')
  })

  it('"Use defaults for everything" appears only for a user with own settings', async () => {
    const bridge = makeBridge()
    const w = mount(TUserUsageLimitsPanel, { props: { userId: 'u1', bridge: bridge as never } })
    await flushPromises()
    expect(buttonByText(w, 'Use defaults for everything')).toBeUndefined()

    bridge.getUser.mockResolvedValueOnce(userLimits({ hasCustomPolicy: true }))
    const w2 = mount(TUserUsageLimitsPanel, { props: { userId: 'u1', bridge: bridge as never } })
    await flushPromises()
    expect(buttonByText(w2, 'Use defaults for everything')).toBeDefined()
    expect(w2.text()).toContain('Own settings')
  })

  it('saves the user\'s own settings and reports the fresh state', async () => {
    const bridge = makeBridge()
    const w = mount(TUserUsageLimitsPanel, { props: { userId: 'u1', bridge: bridge as never } })
    await flushPromises()

    const draft = formOf(w).props('modelValue') as SaveUsagePolicyDto
    formOf(w).vm.$emit('update:modelValue', { ...draft, weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: null } })
    await flushPromises()
    await buttonByText(w, 'Save')!.trigger('click')
    await flushPromises()

    expect(bridge.saveUser).toHaveBeenCalledWith('u1', expect.objectContaining({
      weeklyLimit: { mode: UsageLimitMode.Unlimited, amountUsd: null },
    }))
    expect(w.emitted('updated')?.[0]?.[0]).toMatchObject({ hasCustomPolicy: true })
  })

  it('a default model the user may not use is refused before the round trip', async () => {
    const bridge = makeBridge()
    const w = mount(TUserUsageLimitsPanel, { props: { userId: 'u1', bridge: bridge as never } })
    await flushPromises()

    const draft = formOf(w).props('modelValue') as SaveUsagePolicyDto
    formOf(w).vm.$emit('update:modelValue', { ...draft, allowedModels: ['claude-opus-5-5'], defaultModel: 'claude-sonnet-5-5' })
    await flushPromises()

    expect(w.text()).toContain('The default model must be one of the allowed models.')
  })

  it("keeps the previous user's late answer out of the form", async () => {
    const bridge = makeBridge()
    let resolveA: (v: UserUsageLimitsDto) => void = () => {}
    bridge.getUser.mockImplementation((id: string) =>
      id === 'user-a'
        ? new Promise<UserUsageLimitsDto>((resolve) => { resolveA = resolve })
        : Promise.resolve(userLimits({ userId: 'user-b', hasCustomPolicy: true })),
    )
    const w = mount(TUserUsageLimitsPanel, { props: { userId: 'user-a', bridge: bridge as never } })
    await flushPromises()
    await w.setProps({ userId: 'user-b' })
    await flushPromises()
    resolveA(userLimits({ userId: 'user-a', hasCustomPolicy: false }))
    await flushPromises()

    expect(w.text()).toContain('Own settings')
  })
})
