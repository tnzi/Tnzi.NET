import { describe, it, expect, vi } from 'vitest'
import { createSubscriptionPlanBridge } from '../../../src/services/bridges/subscription-plan-bridge'

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data, message: null })
const refused = (code: number, message: string) => ({ succeeded: false, success: false, code, data: null, message })

const plan = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  planCode: `code-${id}`,
  productCode: null,
  planName: `Plan ${id}`,
  description: null,
  price: 10,
  currency: 'USD',
  cycleType: 'Month',
  cycleValue: 1,
  trialDays: 0,
  allowTrial: false,
  trialDiscount: null,
  sortOrder: 0,
  isActive: true,
  ...over,
})

function mockApi(overrides: Record<string, unknown> = {}) {
  return {
    getPlans: vi.fn(async () => ok([plan('a', { planName: 'Basic' }), plan('b', { planName: 'Pro', productCode: 'saas' })])),
    createPlan: vi.fn(async (d: unknown) => ok({ ...(d as object), id: 'new' })),
    updatePlan: vi.fn(async () => ok(null)),
    deletePlan: vi.fn(async () => ok(null)),
    reorderPlans: vi.fn(async () => ok(null)),
    ...overrides,
  } as never
}

const query = (over: Record<string, unknown> = {}) => ({ pageIndex: 1, pageSize: 20, searchText: '', filters: {}, ...over }) as never

describe('subscription-plan-bridge', () => {
  it('fetch reads every plan (activeOnly=false) and pages them in memory', async () => {
    const api = mockApi()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: api })
    const page = await bridge.fetch(query())
    expect((api as { getPlans: ReturnType<typeof vi.fn> }).getPlans).toHaveBeenCalledWith(false)
    expect(page.items.map((p) => p.id)).toEqual(['a', 'b'])
    expect(page.totalCount).toBe(2)
  })

  it('fetch filters by the keyword against the fields the card shows', async () => {
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: mockApi() })
    const page = await bridge.fetch(query({ searchText: 'saas' }))
    expect(page.items.map((p) => p.id)).toEqual(['b'])
  })

  it('fetch rejects a refused read instead of resolving an empty catalogue', async () => {
    const bridge = createSubscriptionPlanBridge({
      adminSubscriptionApi: mockApi({ getPlans: vi.fn(async () => refused(403, 'Forbidden')) }),
    })
    await expect(bridge.fetch(query())).rejects.toThrow('Forbidden')
  })

  it('create rejects with the server code on a duplicate plan code', async () => {
    const bridge = createSubscriptionPlanBridge({
      adminSubscriptionApi: mockApi({
        createPlan: vi.fn(async () => refused(409, 'SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS')),
      }),
    })
    await expect(bridge.create(plan('x') as never)).rejects.toThrow('SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS')
  })

  it('update sends the whole row and echoes it back on success', async () => {
    const api = mockApi()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: api })
    const row = plan('a', { price: 25, sortOrder: 3 })
    const saved = await bridge.update('a', row as never)
    expect((api as { updatePlan: ReturnType<typeof vi.fn> }).updatePlan).toHaveBeenCalledWith('a', row)
    expect(saved).toMatchObject({ id: 'a', price: 25, sortOrder: 3 })
  })

  it('update rejects when the plan has live subscriptions and the terms changed', async () => {
    const bridge = createSubscriptionPlanBridge({
      adminSubscriptionApi: mockApi({
        updatePlan: vi.fn(async () => refused(400, 'SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS')),
      }),
    })
    await expect(bridge.update('a', plan('a') as never)).rejects.toThrow('SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS')
  })

  it('deactivate calls DELETE and surfaces a refusal', async () => {
    const api = mockApi({ deletePlan: vi.fn(async () => refused(400, 'SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS')) })
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: api })
    await expect(bridge.deactivate('a')).rejects.toThrow('SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS')
    expect((api as { deletePlan: ReturnType<typeof vi.fn> }).deletePlan).toHaveBeenCalledWith('a')
  })

  it('reorder posts the ids in their new order', async () => {
    const api = mockApi()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: api })
    await bridge.reorder(['b', 'a'])
    expect((api as { reorderPlans: ReturnType<typeof vi.fn> }).reorderPlans).toHaveBeenCalledWith({ ids: ['b', 'a'] })
  })

  it('without deps every member rejects', async () => {
    const bridge = createSubscriptionPlanBridge()
    await expect(bridge.fetch(query())).rejects.toThrow(/no HttpClient/)
    await expect(bridge.reorder(['a'])).rejects.toThrow(/no HttpClient/)
  })
})
