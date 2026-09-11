/**
 * Feature bridge - `Tnzi.Feature` feature flags for the admin console.
 *
 * Three sub-contracts, one per backend controller:
 *   - definitions → `/admin/feature-definitions` (CRUD; code-defined rows are
 *                   read-only and the backend rejects edit/delete on them)
 *   - values      → `/admin/feature-values` (what each flag resolves to per
 *                   scope, plus the registered scopes and set / clear)
 *   - usage       → `/admin/feature/usage` (ranking, per-feature totals, trend,
 *                   retention cleanup)
 *
 * ★ Every write goes through `ensureOk` before unwrapping. `HttpClient` hands a
 *   400 back as a failure envelope rather than throwing, and the value
 *   endpoints answer 400 for the one mistake that used to be invisible: a value
 *   written to a scope nothing reads (unknown provider, tenant scope with
 *   multi-tenancy off, wrong key shape). Swallowing that envelope would turn
 *   the refusal back into the silent "saved" it replaces.
 *
 * Definitions and the per-scope value view are fetched whole (the backend does
 * not page them; a deployment has tens of flags, not thousands) and paged /
 * keyword-filtered client-side.
 */
import {
  useAdminFeatureDefinitionApi,
  useAdminFeatureUsageApi,
  useAdminFeatureValueApi,
  type CreateFeatureDefinitionDto,
  type FeatureDefinitionDto,
  type FeaturePopularityDto,
  type FeatureUsagePeriod,
  type FeatureUsageRangeQuery,
  type FeatureUsageStatsDto,
  type FeatureUsageTrendDto,
  type FeatureValueDto,
  type FeatureValueProviderDto,
  type FeatureValueWithDefinitionDto,
  type SetFeatureValueDto,
  type UpdateFeatureDefinitionDto,
} from '@tnzi/core/services/feature'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, pageArray, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminFeatureDefinitionApi>[0]

export type {
  FeatureDefinitionDto,
  CreateFeatureDefinitionDto,
  UpdateFeatureDefinitionDto,
  FeatureValueDto,
  FeatureValueProviderDto,
  FeatureValueWithDefinitionDto,
  FeaturePopularityDto,
  FeatureUsageStatsDto,
  FeatureUsageTrendDto,
  FeatureUsagePeriod,
  FeatureUsageRangeQuery,
}

/** A (providerName, providerKey) pair - the unit values are read and written for. */
export interface FeatureValueScope {
  providerName: string
  providerKey?: string | null
}

export interface FeatureBridgeDeps {
  /** Production path: provide HttpClient; the bridge builds the APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. */
  definitionApi?: ReturnType<typeof useAdminFeatureDefinitionApi>
  valueApi?: ReturnType<typeof useAdminFeatureValueApi>
  usageApi?: ReturnType<typeof useAdminFeatureUsageApi>
}

export interface FeatureValuesContract {
  /** Registered scopes with their write constraints - the scope picker's source. */
  providers(): Promise<FeatureValueProviderDto[]>
  /** Every enabled definition with the value the runtime resolves for `scope`. */
  all(scope: FeatureValueScope, query: CrudPageQuery): Promise<CrudPageResult<FeatureValueWithDefinitionDto>>
  /** Create or replace the scope's value for one definition. Throws with the server's reason on 400. */
  set(data: SetFeatureValueDto): Promise<FeatureValueDto>
  /** Remove the scope's own value row so the flag falls back to inherited / default. */
  clear(valueId: string): Promise<void>
}

export interface FeatureUsageContract {
  mostUsed(top?: number, range?: FeatureUsageRangeQuery): Promise<FeaturePopularityDto[]>
  stats(featureName: string, range?: FeatureUsageRangeQuery): Promise<FeatureUsageStatsDto | null>
  trend(featureName: string, period?: FeatureUsagePeriod, range?: FeatureUsageRangeQuery): Promise<FeatureUsageTrendDto[]>
  /** Delete usage records older than `retentionDays`; resolves to the deleted count. */
  cleanup(retentionDays: number): Promise<number>
}

export interface FeatureBridge {
  definitions: BridgeCrudContract<FeatureDefinitionDto, CreateFeatureDefinitionDto, UpdateFeatureDefinitionDto>
  values: FeatureValuesContract
  usage: FeatureUsageContract
}

const unavailable = (name: string) => (): Promise<never> =>
  Promise.reject(new Error(`feature-bridge: ${name} - no deps provided`))

/** Case-insensitive keyword match over the fields a person searches flags by. */
function matchesKeyword(keyword: string, ...fields: Array<string | null | undefined>): boolean {
  if (!keyword) return true
  return fields.some((f) => (f ?? '').toLowerCase().includes(keyword))
}

function keywordOf(query: CrudPageQuery): string {
  return typeof query.searchText === 'string' ? query.searchText.trim().toLowerCase() : ''
}

export function createFeatureBridge(deps: FeatureBridgeDeps = {}): FeatureBridge {
  const definitionApi = deps.definitionApi ?? (deps.client ? useAdminFeatureDefinitionApi(deps.client) : null)
  const valueApi = deps.valueApi ?? (deps.client ? useAdminFeatureValueApi(deps.client) : null)
  const usageApi = deps.usageApi ?? (deps.client ? useAdminFeatureUsageApi(deps.client) : null)

  if (!definitionApi || !valueApi || !usageApi) {
    return {
      definitions: {
        fetch: unavailable('definitions.fetch') as never,
        create: unavailable('definitions.create'),
        update: unavailable('definitions.update'),
        delete: unavailable('definitions.delete'),
      },
      values: {
        providers: unavailable('values.providers'),
        all: unavailable('values.all') as never,
        set: unavailable('values.set'),
        clear: unavailable('values.clear'),
      },
      usage: {
        mostUsed: unavailable('usage.mostUsed'),
        stats: unavailable('usage.stats'),
        trend: unavailable('usage.trend'),
        cleanup: unavailable('usage.cleanup'),
      },
    }
  }

  const definitions: FeatureBridge['definitions'] = {
    fetch: async (query) => {
      const res = await definitionApi.getAll()
      ensureOk(res)
      const items = unwrap<FeatureDefinitionDto[]>(res) ?? []
      const keyword = keywordOf(query)
      const filtered = items.filter((f) => matchesKeyword(keyword, f.name, f.displayName, f.group))
      return pageArray(filtered, query)
    },
    create: async (data) => {
      const res = await definitionApi.create(data)
      ensureOk(res)
      return unwrap<FeatureDefinitionDto>(res)
    },
    update: async (id, data) => {
      const res = await definitionApi.update(String(id), data)
      ensureOk(res)
      return unwrap<FeatureDefinitionDto>(res)
    },
    delete: async (ids) => {
      // No batch endpoint on the backend - one call per id, stopping at the first refusal.
      for (const id of ids) {
        ensureOk(await definitionApi.delete(String(id)))
      }
    },
  }

  const values: FeatureValuesContract = {
    providers: async () => {
      const res = await valueApi.getProviders()
      ensureOk(res)
      return unwrap<FeatureValueProviderDto[]>(res) ?? []
    },
    all: async (scope, query) => {
      const res = await valueApi.getAll(scope.providerName, scope.providerKey ?? undefined)
      ensureOk(res)
      const items = unwrap<FeatureValueWithDefinitionDto[]>(res) ?? []
      const keyword = keywordOf(query)
      const filtered = items.filter((v) => matchesKeyword(keyword, v.featureName, v.displayName, v.group))
      return pageArray(filtered, query)
    },
    set: async (data) => {
      const res = await valueApi.set(data)
      ensureOk(res)
      return unwrap<FeatureValueDto>(res)
    },
    clear: async (valueId) => {
      ensureOk(await valueApi.delete(valueId))
    },
  }

  const usage: FeatureUsageContract = {
    mostUsed: async (top = 10, range = {}) => {
      const res = await usageApi.getMostUsed(top, range)
      ensureOk(res)
      return unwrap<FeaturePopularityDto[]>(res) ?? []
    },
    stats: async (featureName, range = {}) => {
      const res = await usageApi.getStats(featureName, range)
      ensureOk(res)
      return unwrap<FeatureUsageStatsDto | null>(res) ?? null
    },
    trend: async (featureName, period = 'daily', range = {}) => {
      const res = await usageApi.getTrend(featureName, period, range)
      ensureOk(res)
      return unwrap<FeatureUsageTrendDto[]>(res) ?? []
    },
    cleanup: async (retentionDays) => {
      const res = await usageApi.cleanup(retentionDays)
      ensureOk(res)
      return unwrap<number>(res) ?? 0
    },
  }

  return { definitions, values, usage }
}
