/**
 * Identity bridge — full PoC (Phase 2b Task 2.28).
 *
 * Wires ui-admin CRUD page contracts to @tnzi/core identity admin APIs.
 * The real `@tnzi/core` identity module exports factory functions that take
 * an HttpClient and return an API object — NOT singleton services. This bridge
 * accepts either a client (and builds the APIs internally) or pre-built API
 * objects (so tests can inject mocks without an HttpClient).
 */
import {
  useAdminUserApi,
  useAdminRoleApi,
  useAdminTenantApi,
  useAdminLoginLogApi,
  useAdminOrganizationApi,
  useAdminSessionApi,
  type UserListItemDto,
  type CreateUserDto,
  type UpdateUserDto,
  type UserListQueryDto,
  type RoleDto,
  type CreateRoleDto,
  type UpdateRoleDto,
  type RoleListQueryDto,
  type TenantDto,
  type CreateTenantDto,
  type UpdateTenantDto,
  type TenantQueryDto,
  type LoginLogDto,
  type LoginLogQueryDto,
  type OrganizationDto as CoreOrganizationDto,
  type OrganizationTreeNodeDto,
  type CreateOrganizationDto,
  type UpdateOrganizationDto,
  type UserSessionDto,
  type SessionStatisticsDto,
} from '@tnzi/core/services/identity'
import type { ApiResult, PagedList } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'

// HttpClient type derived from the factory signature so we don't need a separate import.
type HttpClient = Parameters<typeof useAdminUserApi>[0]

export interface IdentityBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. If provided, `client` is ignored for that API. */
  userApi?: ReturnType<typeof useAdminUserApi>
  roleApi?: ReturnType<typeof useAdminRoleApi>
  tenantApi?: ReturnType<typeof useAdminTenantApi>
  loginLogApi?: ReturnType<typeof useAdminLoginLogApi>
  organizationApi?: ReturnType<typeof useAdminOrganizationApi>
  sessionApi?: ReturnType<typeof useAdminSessionApi>
}

/** A pending GDPR request from a user (admin view). */
export interface GdprRequestDto {
  id: string
  userId: string
  username: string
  requestType: 'export' | 'deletion'
  status: 'pending' | 'approved' | 'denied'
  requestedAt: string
  notes?: string
}

// Re-export core's OrganizationDto for bridge consumers.
export type OrganizationDto = CoreOrganizationDto
export type { OrganizationTreeNodeDto, CreateOrganizationDto, UpdateOrganizationDto } from '@tnzi/core/services/identity'

export interface SessionDto {
  id: string
  userName?: string
  ip?: string
  userAgent?: string
  location?: string
  loginTime?: string
  lastActiveAt?: string
  isActive?: boolean
}

export interface IdentityBridge {
  users: BridgeCrudContract<UserListItemDto, CreateUserDto, UpdateUserDto> & {
    /** Enable a user account (sets isEnabled=true). */
    enable(id: string): Promise<void>
    /** Disable a user account (sets isEnabled=false). Optional reason recorded in audit log. */
    disable(id: string, reason?: string | null): Promise<void>
    /** Lock a user account. `until` null = permanent lock. */
    lock(id: string, until?: string | null, reason?: string | null): Promise<void>
    /** Unlock a previously locked user account. */
    unlock(id: string): Promise<void>
    /** Admin-side password reset. The user must change it on next login. */
    resetPassword(id: string, newPassword: string): Promise<void>
  }
  roles: BridgeCrudContract<RoleDto, CreateRoleDto, UpdateRoleDto>
  tenants: BridgeCrudContract<TenantDto, CreateTenantDto, UpdateTenantDto>
  /**
   * Organizations are a hierarchical tree, not a paged list — the surface is
   * tree-shaped (getTree/move/getChildren) rather than BridgeCrudContract.
   * Wires the full DefaultOrganizationAdminController endpoint set.
   */
  organizations: {
    getTree(): Promise<OrganizationTreeNodeDto[]>
    getById(id: string): Promise<OrganizationDto>
    create(data: CreateOrganizationDto): Promise<OrganizationDto>
    update(id: string, data: UpdateOrganizationDto): Promise<OrganizationDto>
    delete(id: string): Promise<void>
    /** Move under a new parent. `newParentId=null` makes the node a root. */
    move(id: string, newParentId: string | null): Promise<void>
    getChildren(id: string): Promise<OrganizationDto[]>
    search(keyword: string, maxResults?: number): Promise<OrganizationDto[]>
  }
  sessions: {
    /** Per-user session list (admin can pull any user's sessions). */
    listForUser(userId: string, includeRevoked?: boolean): Promise<UserSessionDto[]>
    /** Aggregate online/active/expired counts across all users. */
    statistics(): Promise<SessionStatisticsDto>
    /** Force-revoke a single session (logs the target user out of that device). */
    revoke(sessionId: string): Promise<void>
    /** Revoke every session for a user except optionally the current one. */
    revokeAllForUser(userId: string, excludeSessionId?: string | null): Promise<void>
    /** Sweep sessions inactive for N minutes (defaults to backend policy). */
    cleanExpired(inactiveMinutes?: number): Promise<number>
  }
  loginLogs: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<LoginLogDto>>
  }
  gdpr: {
    requestExport(userId: string): Promise<Blob>
    requestDeletion(userId: string): Promise<void>
    /** List pending/processed GDPR requests (admin view). */
    fetchRequests(query: CrudPageQuery): Promise<CrudPageResult<GdprRequestDto>>
    /** Approve a GDPR request by ID. */
    approveRequest(id: string): Promise<void>
    /** Deny a GDPR request by ID. */
    denyRequest(id: string): Promise<void>
  }
}

function mapQuery(q: CrudPageQuery): Record<string, unknown> {
  return {
    pageIndex: q.pageIndex,
    pageSize: q.pageSize,
    keyword: q.searchText || undefined,
    sortBy: q.sortField,
    sortOrder: q.sortOrder ?? undefined,
    ...q.filters,
  }
}

function unwrap<T>(res: ApiResult<T> | T): T {
  // HttpClient methods return ApiResult<T>; test mocks may return the payload directly.
  if (res && typeof res === 'object' && 'data' in (res as object) && 'succeeded' in (res as object)) {
    return (res as ApiResult<T>).data
  }
  return res as T
}

function toCrudResult<T>(p: PagedList<T>): CrudPageResult<T> {
  return {
    items: p.items,
    totalCount: p.totalCount,
    pageIndex: p.pageIndex,
    pageSize: p.pageSize,
  }
}

export function createIdentityBridge(deps: IdentityBridgeDeps = {}): IdentityBridge {
  const userApi = deps.userApi ?? (deps.client ? useAdminUserApi(deps.client) : null)
  const roleApi = deps.roleApi ?? (deps.client ? useAdminRoleApi(deps.client) : null)
  const tenantApi = deps.tenantApi ?? (deps.client ? useAdminTenantApi(deps.client) : null)
  const loginLogApi =
    deps.loginLogApi ?? (deps.client ? useAdminLoginLogApi(deps.client) : null)
  // organizationApi + sessionApi are optional — they back the
  // organizations / sessions sub-contracts only. If neither a client
  // nor an explicit mock is supplied, the sub-contract methods reject
  // with a clear error, leaving the rest of the bridge usable. This
  // keeps the existing 4-api test fixtures passing.
  const organizationApi =
    deps.organizationApi ?? (deps.client ? useAdminOrganizationApi(deps.client) : null)
  const sessionApi =
    deps.sessionApi ?? (deps.client ? useAdminSessionApi(deps.client) : null)

  if (!userApi || !roleApi || !tenantApi || !loginLogApi) {
    throw new Error(
      'createIdentityBridge: provide either `client` (HttpClient) or all four api deps (userApi/roleApi/tenantApi/loginLogApi)',
    )
  }

  // Helper: lazy-reject when an optional sub-api wasn't wired.
  const missing = <T>(label: string): Promise<T> =>
    Promise.reject(new Error(`identity-bridge: ${label} requires an HttpClient or explicit api mock`))

  const users: IdentityBridge['users'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<UserListItemDto>>(
          await userApi.getList(mapQuery(q) as unknown as UserListQueryDto),
        ),
      ),
    create: async (data) => unwrap(await userApi.create(data)) as UserListItemDto,
    update: async (id, data) => unwrap(await userApi.update(id, data)) as UserListItemDto,
    delete: async (ids) => {
      await userApi.deleteMany(ids)
    },
    export: async (q) => {
      const csv = unwrap<string>(
        await userApi.exportCsv(mapQuery(q) as unknown as UserListQueryDto),
      )
      return new Blob([csv], { type: 'text/csv' })
    },
    import: async (file) => {
      await userApi.importCsv(file)
    },
    enable: async (id) => {
      await userApi.enable(id)
    },
    disable: async (id, reason) => {
      await userApi.disable(id, reason ?? null)
    },
    lock: async (id, until, reason) => {
      await userApi.lock(id, { lockoutEnd: until ?? null, reason: reason ?? null })
    },
    unlock: async (id) => {
      await userApi.unlock(id)
    },
    resetPassword: async (id, newPassword) => {
      await userApi.resetPassword(id, { newPassword })
    },
  }

  const roles: BridgeCrudContract<RoleDto, CreateRoleDto, UpdateRoleDto> = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<RoleDto>>(
          await roleApi.getPagedList(mapQuery(q) as unknown as RoleListQueryDto),
        ),
      ),
    create: async (data) => unwrap(await roleApi.create(data)) as RoleDto,
    update: async (id, data) => unwrap(await roleApi.update(id, data)) as RoleDto,
    delete: async (ids) => {
      await roleApi.deleteMany(ids)
    },
  }

  const tenants: BridgeCrudContract<TenantDto, CreateTenantDto, UpdateTenantDto> = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<TenantDto>>(
          await tenantApi.getPagedList(mapQuery(q) as unknown as TenantQueryDto),
        ),
      ),
    create: async (data) => unwrap(await tenantApi.create(data)) as TenantDto,
    update: async (id, data) => unwrap(await tenantApi.update(id, data)) as TenantDto,
    delete: async (ids) => {
      // Tenant admin API has no batch delete — loop sequentially.
      for (const id of ids) {
        await tenantApi.delete(id)
      }
    },
  }

  const loginLogs = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<LoginLogDto>> =>
      toCrudResult(
        unwrap<PagedList<LoginLogDto>>(
          await loginLogApi.getList(mapQuery(q) as unknown as LoginLogQueryDto),
        ),
      ),
  }

  // TODO(phase-3): admin GDPR endpoints currently only exist for the current user
  // via profileApi (exportPersonalData / deleteAccount). When backend adds
  // admin-by-id GDPR endpoints, route them here.
  // fetchRequests / approveRequest / denyRequest are stub-only until backend ships
  // the admin GDPR management endpoints (tracked as a backend task).
  const gdpr = {
    requestExport: async (_userId: string): Promise<Blob> => {
      throw new Error(
        'GDPR export by userId not supported until admin endpoints land',
      )
    },
    requestDeletion: async (_userId: string): Promise<void> => {
      throw new Error(
        'GDPR delete by userId not supported until admin endpoints land',
      )
    },
    fetchRequests: async (_query: CrudPageQuery): Promise<CrudPageResult<GdprRequestDto>> => {
      throw new Error(
        'GDPR request list not supported until admin endpoints land',
      )
    },
    approveRequest: async (_id: string): Promise<void> => {
      throw new Error(
        'GDPR approve not supported until admin endpoints land',
      )
    },
    denyRequest: async (_id: string): Promise<void> => {
      throw new Error(
        'GDPR deny not supported until admin endpoints land',
      )
    },
  }

  const organizations: IdentityBridge['organizations'] = organizationApi
    ? {
        getTree: async () =>
          unwrap(await organizationApi.getTree()) as OrganizationTreeNodeDto[],
        getById: async (id) => unwrap(await organizationApi.getById(id)) as OrganizationDto,
        create: async (data) => unwrap(await organizationApi.create(data)) as OrganizationDto,
        update: async (id, data) =>
          unwrap(await organizationApi.update(id, data)) as OrganizationDto,
        delete: async (id) => {
          await organizationApi.delete(id)
        },
        move: async (id, newParentId) => {
          await organizationApi.move(id, { newParentId })
        },
        getChildren: async (id) =>
          unwrap(await organizationApi.getChildren(id)) as OrganizationDto[],
        search: async (keyword, maxResults) =>
          unwrap(await organizationApi.search(keyword, maxResults)) as OrganizationDto[],
      }
    : {
        getTree: () => missing('organizations.getTree'),
        getById: () => missing('organizations.getById'),
        create: () => missing('organizations.create'),
        update: () => missing('organizations.update'),
        delete: () => missing('organizations.delete'),
        move: () => missing('organizations.move'),
        getChildren: () => missing('organizations.getChildren'),
        search: () => missing('organizations.search'),
      }

  const sessions: IdentityBridge['sessions'] = sessionApi
    ? {
        listForUser: async (userId, includeRevoked) =>
          unwrap(await sessionApi.getUserSessions(userId, { includeRevoked })) as UserSessionDto[],
        statistics: async () => unwrap(await sessionApi.getStatistics()) as SessionStatisticsDto,
        revoke: async (sessionId) => {
          await sessionApi.revokeSession(sessionId)
        },
        revokeAllForUser: async (userId, excludeSessionId) => {
          await sessionApi.revokeAllSessions(userId, excludeSessionId ?? null)
        },
        cleanExpired: async (inactiveMinutes) =>
          unwrap(await sessionApi.cleanExpired(inactiveMinutes)) as number,
      }
    : {
        listForUser: () => missing('sessions.listForUser'),
        statistics: () => missing('sessions.statistics'),
        revoke: () => missing('sessions.revoke'),
        revokeAllForUser: () => missing('sessions.revokeAllForUser'),
        cleanExpired: () => missing('sessions.cleanExpired'),
      }

  return { users, roles, tenants, organizations, sessions, loginLogs, gdpr }
}
