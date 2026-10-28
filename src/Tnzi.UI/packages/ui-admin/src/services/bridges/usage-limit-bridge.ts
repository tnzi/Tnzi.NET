/**
 * Usage-limit bridge: everyone's defaults, one user's own settings, the users
 * who used AI, and the tenant's cost budget.
 *
 * Its own module (and not only a sub-contract of `createAiBridge`) because the
 * two usage-limit panels are exported from the package root so a consumer can
 * host them: building the whole AI bridge there would put every AI admin
 * endpoint factory on the shell's static graph. `createAiBridge().usageLimits`
 * is this same bridge.
 */
import type { HttpClient } from '@tnzi/core/http'
import type { PagedList } from '@tnzi/core'
import {
  useAdminUsageLimitApi,
  type BudgetSummaryDto,
  type SaveUsagePolicyDto,
  type UsageLimitDefaultsDto,
  type UsageMeterQueryDto,
  type UserUsageLimitsDto,
  type UserUsageSummaryDto,
} from '@tnzi/core/services/ai'
import type { CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, unwrapOk } from '../_mappers'

export interface UsageLimitBridge {
  getDefaults(): Promise<UsageLimitDefaultsDto>
  saveDefaults(data: SaveUsagePolicyDto): Promise<UsageLimitDefaultsDto>
  getUser(userId: string): Promise<UserUsageLimitsDto>
  saveUser(userId: string, data: SaveUsagePolicyDto): Promise<UserUsageLimitsDto>
  /** Drop the user's own settings: every item follows the defaults again. */
  clearUser(userId: string): Promise<void>
  /** Clear the user's current windows (their usage starts again from zero). */
  resetUser(userId: string): Promise<void>
  /** Users who used AI, most recent first, with their windows (filter: `filters.userId`). */
  fetchUsers(query: CrudPageQuery): Promise<CrudPageResult<UserUsageSummaryDto>>
  /** USD cost budget summary for a tenant/time-range. */
  getBudgetSummary(params?: { tenantId?: string; startTime?: string; endTime?: string }): Promise<BudgetSummaryDto>
}

export interface UsageLimitBridgeDeps {
  client?: HttpClient
  usageLimitApi?: ReturnType<typeof useAdminUsageLimitApi>
}

export function createUsageLimitBridge(deps: UsageLimitBridgeDeps): UsageLimitBridge {
  const api = deps.usageLimitApi ?? (deps.client ? useAdminUsageLimitApi(deps.client) : null)
  if (!api) throw new Error('createUsageLimitBridge: provide either `client` (HttpClient) or `usageLimitApi`')

  return {
    getDefaults: async () => unwrapOk<UsageLimitDefaultsDto>(await api.getDefaults()),
    saveDefaults: async (data) => unwrapOk<UsageLimitDefaultsDto>(await api.saveDefaults(data)),
    getUser: async (userId) => unwrapOk<UserUsageLimitsDto>(await api.getUser(userId)),
    saveUser: async (userId, data) => unwrapOk<UserUsageLimitsDto>(await api.saveUser(userId, data)),
    clearUser: async (userId) => {
      ensureOk(await api.clearUser(userId))
    },
    resetUser: async (userId) => {
      ensureOk(await api.resetUser(userId))
    },
    fetchUsers: async (q) => {
      const filters = (q.filters ?? {}) as Record<string, unknown>
      const pageIndex = q.pageIndex ?? 1
      const pageSize = q.pageSize ?? 20
      const query: UsageMeterQueryDto = {
        pageIndex,
        pageSize,
        skip: (pageIndex - 1) * pageSize,
        take: pageSize,
        userId: (filters.userId as string | undefined) || null,
      }
      return unwrapOk<PagedList<UserUsageSummaryDto>>(await api.queryUsers(query))
    },
    // unwrapOk, not a bare unwrap: a refused read must not reach the page as a
    // missing summary, which the budget tab would paint as "Within budget, $0.00".
    getBudgetSummary: async (params) => unwrapOk<BudgetSummaryDto>(await api.getBudgetSummary(params)),
  }
}
