/**
 * Login security bridge — wraps `/admin/login-security/*` exposed by
 * `Tnzi.Identity.Controllers.Admin.DefaultLoginSecurityAdminController`.
 *
 * Read endpoints for the security overview dashboard:
 *   • GET /admin/login-security/overview           — KPIs over a time window
 *   • GET /admin/login-security/frequent-failures  — users hitting failure threshold
 *   • GET /admin/login-security/user/{userId}/recent-logins
 *   • GET /admin/login-security/user/{userId}/frequent-ips
 */
import type { HttpClient } from '@tnzi/core/http'

export interface SecurityOverviewDto {
  timeRangeHours: number
  totalLoginAttempts: number
  successfulLogins: number
  failedLogins: number
  failureRate: number
  distinctUsers: number
  distinctIpAddresses: number
  lockedOutUsers: number
}

export interface UserFailedLoginSummaryDto {
  userId: string
  userName?: string | null
  email?: string | null
  failureCount: number
  lastFailureTime?: string | null
  ipAddresses: string[]
}

export interface LoginSecurityBridgeDeps {
  client?: HttpClient
}

export interface LoginSecurityBridge {
  getOverview(hours?: number): Promise<SecurityOverviewDto | null>
  getFrequentFailures(hours?: number, minFailures?: number): Promise<UserFailedLoginSummaryDto[]>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
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

  return {
    getOverview: async (hours = 24) =>
      unwrap<SecurityOverviewDto | null>(
        await client.get<SecurityOverviewDto>(`/admin/login-security/overview?hours=${hours}`),
      ),
    getFrequentFailures: async (hours = 24, minFailures = 3) =>
      unwrap<UserFailedLoginSummaryDto[]>(
        await client.get<UserFailedLoginSummaryDto[]>(
          `/admin/login-security/frequent-failures?hours=${hours}&minFailures=${minFailures}`,
        ),
      ) ?? [],
  }
}
