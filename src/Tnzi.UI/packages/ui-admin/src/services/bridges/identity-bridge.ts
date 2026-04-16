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
} from '@tnzi/core/services/identity'
import type { ApiResult, PagedList } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'

// HttpClient type derived from the factory signature so we don't need a separate import.
type HttpClient = Parameters<typeof useAdminUserApi>[0]

export interface IdentityBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all 4 APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. If provided, `client` is ignored for that API. */
  userApi?: ReturnType<typeof useAdminUserApi>
  roleApi?: ReturnType<typeof useAdminRoleApi>
  tenantApi?: ReturnType<typeof useAdminTenantApi>
  loginLogApi?: ReturnType<typeof useAdminLoginLogApi>
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

export interface IdentityBridge {
  users: BridgeCrudContract<UserListItemDto, CreateUserDto, UpdateUserDto>
  roles: BridgeCrudContract<RoleDto, CreateRoleDto, UpdateRoleDto>
  tenants: BridgeCrudContract<TenantDto, CreateTenantDto, UpdateTenantDto>
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

  if (!userApi || !roleApi || !tenantApi || !loginLogApi) {
    throw new Error(
      'createIdentityBridge: provide either `client` (HttpClient) or all four api deps (userApi/roleApi/tenantApi/loginLogApi)',
    )
  }

  const users: BridgeCrudContract<UserListItemDto, CreateUserDto, UpdateUserDto> = {
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

  return { users, roles, tenants, loginLogs, gdpr }
}
