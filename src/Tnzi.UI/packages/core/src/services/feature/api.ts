/**
 * Feature Module API - admin access to feature flags.
 *
 * Mirrors the three admin controllers of `Tnzi.Feature`:
 *   - `DefaultFeatureDefinitionAdminController` → `/admin/feature-definitions`
 *   - `DefaultFeatureValueAdminController`      → `/admin/feature-values`
 *   - `DefaultFeatureUsageAdminController`      → `/admin/feature/usage`
 *
 * `Tnzi.Feature` is an optional module: every endpoint answers 404 when the host
 * application does not load it. All three surfaces sit behind `feature.view`;
 * writes additionally need `feature.update` (definitions, values) or
 * `feature.delete` (delete definition / value, usage cleanup).
 */

import type { HttpClient } from '../../http/http';
import type {
  BatchSetFeatureValuesDto,
  BatchSetFeatureValuesResultDto,
  CreateFeatureDefinitionDto,
  FeatureDefinitionDto,
  FeaturePopularityDto,
  FeatureUsagePeriod,
  FeatureUsageRangeQuery,
  FeatureUsageStatsDto,
  FeatureUsageTrendDto,
  FeatureValueDto,
  FeatureValueProviderDto,
  FeatureValueWithDefinitionDto,
  SetFeatureValueDto,
  UpdateFeatureDefinitionDto,
} from './types';

const ADMIN_FEATURE_DEFINITIONS_BASE = '/admin/feature-definitions';
const ADMIN_FEATURE_VALUES_BASE = '/admin/feature-values';
const ADMIN_FEATURE_USAGE_BASE = '/admin/feature/usage';

/** Build a query string, skipping null / undefined / empty entries. */
function toQuery(params: Record<string, string | number | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue;
    search.set(key, String(value));
  }
  return search.toString();
}

/**
 * Admin Feature Definition API - CRUD over the flag catalogue.
 *
 * `getAll` returns the merged snapshot: database rows plus code-defined
 * definitions (`source === 'Code'`, `isReadOnly`, id all zeros).
 *
 * Example:
 * ```ts
 * const api = useAdminFeatureDefinitionApi(client);
 * const all = await api.getAll();
 * await api.create({ name: 'Orders.AdvancedSearch', valueType: FeatureValueType.Boolean });
 * ```
 */
export function useAdminFeatureDefinitionApi(client: HttpClient) {
  return {
    /** Every definition (database + code), ordered by group then name. Not paged. */
    getAll: () =>
      client.get<FeatureDefinitionDto[]>(ADMIN_FEATURE_DEFINITIONS_BASE),

    /** One database-backed definition by id. */
    getById: (id: string) =>
      client.get<FeatureDefinitionDto>(`${ADMIN_FEATURE_DEFINITIONS_BASE}/${encodeURIComponent(id)}`),

    /** Create a definition (`feature.create`). 409 when the name exists. */
    create: (data: CreateFeatureDefinitionDto) =>
      client.post<FeatureDefinitionDto>(ADMIN_FEATURE_DEFINITIONS_BASE, data),

    /** Update a database-backed definition (`feature.update`). */
    update: (id: string, data: UpdateFeatureDefinitionDto) =>
      client.put<FeatureDefinitionDto>(`${ADMIN_FEATURE_DEFINITIONS_BASE}/${encodeURIComponent(id)}`, data),

    /** Delete a definition and, by cascade, every value attached to it (`feature.delete`). */
    delete: (id: string) =>
      client.delete(`${ADMIN_FEATURE_DEFINITIONS_BASE}/${encodeURIComponent(id)}`),
  };
}

/**
 * Admin Feature Value API - the values a flag resolves to per scope.
 *
 * A scope is a (providerName, providerKey) pair. Only providers listed by
 * `getProviders` are accepted for writes; a keyed provider (Tenant) needs a
 * key, a keyless one (Global) rejects it, and an inactive provider refuses
 * writes outright - all as 400 with the reason in the message.
 *
 * Example:
 * ```ts
 * const api = useAdminFeatureValueApi(client);
 * const scopes = await api.getProviders();
 * const view = await api.getAll('Global');
 * await api.set({ featureDefinitionId, providerName: 'Global', value: 'true' });
 * ```
 */
export function useAdminFeatureValueApi(client: HttpClient) {
  return {
    /** Registered providers (scopes) with their write constraints. */
    getProviders: () =>
      client.get<FeatureValueProviderDto[]>(`${ADMIN_FEATURE_VALUES_BASE}/providers`),

    /** Raw stored value rows for one scope. */
    getValues: (providerName: string, providerKey?: string | null) => {
      const query = toQuery({ providerName, providerKey });
      return client.get<FeatureValueDto[]>(`${ADMIN_FEATURE_VALUES_BASE}?${query}`);
    },

    /**
     * Every enabled definition with the value the runtime resolves for this scope
     * (own value → inherited keyless value → definition default), plus where it
     * comes from. This is the view an admin screen renders.
     */
    getAll: (providerName: string, providerKey?: string | null) => {
      const query = toQuery({ providerName, providerKey });
      return client.get<FeatureValueWithDefinitionDto[]>(`${ADMIN_FEATURE_VALUES_BASE}/all?${query}`);
    },

    /** Create or replace one scope's value for a definition (`feature.update`). */
    set: (data: SetFeatureValueDto) =>
      client.post<FeatureValueDto>(ADMIN_FEATURE_VALUES_BASE, data),

    /** Set several values for one scope in one call; partial success is reported per item. */
    batchSet: (data: BatchSetFeatureValuesDto) =>
      client.post<BatchSetFeatureValuesResultDto>(`${ADMIN_FEATURE_VALUES_BASE}/batch`, data),

    /** Remove a stored value row (`feature.delete`); the scope falls back to inherited / default. */
    delete: (id: string) =>
      client.delete(`${ADMIN_FEATURE_VALUES_BASE}/${encodeURIComponent(id)}`),
  };
}

/**
 * Admin Feature Usage API - analytics over recorded feature checks.
 *
 * Example:
 * ```ts
 * const api = useAdminFeatureUsageApi(client);
 * const top = await api.getMostUsed(10);
 * const stats = await api.getStats('Orders.AdvancedSearch', { from, to });
 * const trend = await api.getTrend('Orders.AdvancedSearch', 'weekly');
 * ```
 */
export function useAdminFeatureUsageApi(client: HttpClient) {
  return {
    /** Totals for one feature over an optional inclusive date range. */
    getStats: (featureName: string, range: FeatureUsageRangeQuery = {}) => {
      const query = toQuery({ featureName, from: range.from, to: range.to });
      return client.get<FeatureUsageStatsDto>(`${ADMIN_FEATURE_USAGE_BASE}/stats?${query}`);
    },

    /** Check counts per bucket; `period` is `daily` (default), `weekly` or `monthly`. */
    getTrend: (featureName: string, period: FeatureUsagePeriod = 'daily', range: FeatureUsageRangeQuery = {}) => {
      const query = toQuery({ featureName, period, from: range.from, to: range.to });
      return client.get<FeatureUsageTrendDto[]>(`${ADMIN_FEATURE_USAGE_BASE}/trend?${query}`);
    },

    /** The `top` most-checked features (1-100, default 10). */
    getMostUsed: (top = 10, range: FeatureUsageRangeQuery = {}) => {
      const query = toQuery({ top, from: range.from, to: range.to });
      return client.get<FeaturePopularityDto[]>(`${ADMIN_FEATURE_USAGE_BASE}/most-used?${query}`);
    },

    /** Delete usage records older than `retentionDays` (`feature.delete`); returns the deleted count. */
    cleanup: (retentionDays = 90) => {
      const query = toQuery({ retentionDays });
      return client.delete<number>(`${ADMIN_FEATURE_USAGE_BASE}/cleanup?${query}`);
    },
  };
}
