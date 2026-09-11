import { describe, it, expect, vi } from 'vitest'
import { createFeatureBridge } from '../../src/services/bridges/feature-bridge'

/**
 * The bridge is where a backend refusal either becomes an error the operator
 * sees, or gets swallowed into a "saved" toast. `HttpClient` returns a 400 as
 * a failure envelope rather than throwing, and the value endpoints answer 400
 * for exactly the mistakes that used to be silent (unknown / inactive scope,
 * wrong key shape), so every write here must throw with the server's message.
 */
const ok = <T,>(data: T) => ({ success: true, code: 200, data })
const refused = (message: string) => ({ success: false, code: 400, message, data: null })

function apis() {
  return {
    definitionApi: {
      getAll: vi.fn(async () => ok([{ id: 'd1', name: 'A.B', displayName: 'Ab', group: 'G' }, { id: 'd2', name: 'C.D', group: 'H' }])),
      getById: vi.fn(),
      create: vi.fn(async () => refused("Feature definition with name 'A.B' already exists")),
      update: vi.fn(async () => ok({ id: 'd1' })),
      delete: vi.fn(async () => ok(undefined)),
    },
    valueApi: {
      getProviders: vi.fn(async () => ok([{ name: 'Global', priority: 100, requiresKey: false, isActive: true }])),
      getValues: vi.fn(),
      getAll: vi.fn(async () => ok([{ featureName: 'A.B', displayName: 'Ab', group: 'G' }, { featureName: 'C.D', group: 'H' }])),
      set: vi.fn(async () => refused("Feature value provider 'Tenant' is inactive: multi-tenancy is disabled")),
      batchSet: vi.fn(),
      delete: vi.fn(async () => ok(undefined)),
    },
    usageApi: {
      getStats: vi.fn(async () => ok(null)),
      getTrend: vi.fn(async () => ok([])),
      getMostUsed: vi.fn(async () => ok([])),
      cleanup: vi.fn(async () => ok(3)),
    },
  }
}

const query = { pageIndex: 1, pageSize: 20, searchText: '', filters: {} }

describe('feature-bridge', () => {
  it('values.set throws with the server reason on a refusal envelope', async () => {
    const bridge = createFeatureBridge(apis() as never)
    await expect(
      bridge.values.set({ featureDefinitionId: 'd1', providerName: 'Tenant', providerKey: 't-1', value: 'true' }),
    ).rejects.toThrow("provider 'Tenant' is inactive")
  })

  it('definitions.create throws with the server reason on a refusal envelope', async () => {
    const bridge = createFeatureBridge(apis() as never)
    await expect(bridge.definitions.create({ name: 'A.B', valueType: 'Boolean' } as never)).rejects.toThrow('already exists')
  })

  it('definitions.fetch filters by keyword across name / displayName / group and pages client-side', async () => {
    const bridge = createFeatureBridge(apis() as never)
    const byGroup = await bridge.definitions.fetch({ ...query, searchText: 'h' })
    expect(byGroup.items.map((d) => d.name)).toEqual(['C.D'])
    const byDisplay = await bridge.definitions.fetch({ ...query, searchText: 'AB' })
    expect(byDisplay.items.map((d) => d.name)).toEqual(['A.B'])
    const all = await bridge.definitions.fetch(query)
    expect(all.totalCount).toBe(2)
  })

  it('values.all passes the scope through and pages the merged view', async () => {
    const a = apis()
    const bridge = createFeatureBridge(a as never)
    const page = await bridge.values.all({ providerName: 'Tenant', providerKey: 't-1' }, { ...query, searchText: 'c.d' })
    expect(a.valueApi.getAll).toHaveBeenCalledWith('Tenant', 't-1')
    expect(page.items.map((v) => v.featureName)).toEqual(['C.D'])
  })

  it('values.all sends undefined, not null, for a keyless scope', async () => {
    const a = apis()
    const bridge = createFeatureBridge(a as never)
    await bridge.values.all({ providerName: 'Global', providerKey: null }, query)
    expect(a.valueApi.getAll).toHaveBeenCalledWith('Global', undefined)
  })

  it('usage.cleanup resolves to the deleted count', async () => {
    const bridge = createFeatureBridge(apis() as never)
    await expect(bridge.usage.cleanup(30)).resolves.toBe(3)
  })

  it('without deps every call rejects instead of silently no-op-ing', async () => {
    const bridge = createFeatureBridge()
    await expect(bridge.values.providers()).rejects.toThrow('no deps provided')
    await expect(bridge.usage.mostUsed()).rejects.toThrow('no deps provided')
  })
})
