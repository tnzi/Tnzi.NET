/**
 * Login security bridge - thin adapter over `@tnzi/core`'s admin
 * login-security API (`useAdminLoginSecurityApi`, wrapping
 * `/admin/login-security/*` exposed by
 * `Tnzi.Identity.Controllers.Admin.DefaultLoginSecurityAdminController`).
 *
 * Delegates to the canonical `useAdminLoginSecurityApi` factory in
 * `@tnzi/core/services/identity` (getOverview / getFrequentFailures). This
 * file keeps the bridge's original public surface (`SecurityOverviewDto` /
 * `UserFailedLoginSummaryDto` re-exports + `createLoginSecurityBridge`) so
 * consuming pages are unaffected.
 *
 * Read endpoints for the security overview dashboard:
 *   • GET /admin/login-security/overview - KPIs over a time window
 *   • GET /admin/login-security/frequent-failures - users hitting failure threshold
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminLoginSecurityApi,
  type SecurityOverviewDto,
  type UserFailedLoginSummaryDto,
} from '@tnzi/core/services/identity'
import { unwrapResult as unwrap } from '../_mappers'

// Re-export under the original bridge names consumed by pages.
export type { SecurityOverviewDto, UserFailedLoginSummaryDto }

export interface LoginSecurityBridgeDeps {
  client?: HttpClient
}

export interface LoginSecurityBridge {
  getOverview(hours?: number): Promise<SecurityOverviewDto | null>
  getFrequentFailures(hours?: number, minFailures?: number): Promise<UserFailedLoginSummaryDto[]>
}

export function createLoginSecurityBridge(deps: LoginSecurityBridgeDeps = {}): LoginSecurityBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createLoginSecurityBridge: no HttpClient provided'))
    return {
      getOverview: noOp as never,
      getFrequentFailures: noOp as never,
    }
  }

  const api = useAdminLoginSecurityApi(client)

  return {
    getOverview: async (hours = 24) =>
      unwrap<SecurityOverviewDto | null>(await api.getOverview(hours)),
    getFrequentFailures: async (hours = 24, minFailures = 3) =>
      unwrap<UserFailedLoginSummaryDto[]>(await api.getFrequentFailures({ hours, minFailures })) ?? [],
  }
}
