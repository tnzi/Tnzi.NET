/**
 * Authorization bridge — full implementation (Phase 3 Task 3.6 + 3.8).
 *
 * Adapts the authorization backend APIs (tree/list/assign patterns) to the
 * BridgeCrudContract shape used by all TCrudPage-based management pages.
 *
 * NOTE: The backend has no paged-list endpoints for these resources; the bridge
 * fetches the full flat list and applies client-side pagination via `pageArray`.
 *
 * Sub-contracts:
 *   - functionModules  → /admin/modules
 *   - permissions      → /admin/modules/{moduleId}/functions  (read-only)
 *   - roleFunctions    → /admin/role-functions  (fetch by functionId)
 *   - entityRoles      → /admin/data-auth/entity-roles (fetch by roleId)
 */
import {
  useAdminFunctionModuleApi,
  useAdminModuleFunctionApi,
  useAdminRoleFunctionApi,
  useAdminEntityRoleApi,
  type FunctionModuleDto,
  type CreateFunctionModuleDto,
  type UpdateFunctionModuleDto,
  type ModuleFunctionDto,
  type RoleFunctionDto,
  type RoleFunctionQueryDto,
  type EntityRoleDto,
  type CreateEntityRoleDto,
  type UpdateEntityRoleDto,
} from '@tnzi/core/services/authorization'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { pageArray } from '../_mappers'

// HttpClient type derived from a factory signature.
type HttpClient = Parameters<typeof useAdminFunctionModuleApi>[0]

export interface AuthorizationBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock API objects directly. */
  functionModuleApi?: ReturnType<typeof useAdminFunctionModuleApi>
  moduleFunctionApi?: ReturnType<typeof useAdminModuleFunctionApi>
  roleFunctionApi?: ReturnType<typeof useAdminRoleFunctionApi>
  entityRoleApi?: ReturnType<typeof useAdminEntityRoleApi>
}

export interface AuthorizationBridge {
  functionModules: BridgeCrudContract<FunctionModuleDto, CreateFunctionModuleDto, UpdateFunctionModuleDto>
  /**
   * Permissions (ModuleFunction) — read-only. Backend has no admin create/update/delete.
   * Requires a `moduleId` filter in the query to fetch permissions for a given module.
   */
  permissions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>>
  }
  roleFunctions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<RoleFunctionDto>>
  }
  entityRoles: BridgeCrudContract<EntityRoleDto, CreateEntityRoleDto, UpdateEntityRoleDto>
}

function unwrap<T>(res: T | { data: T; succeeded: boolean }): T {
  if (res && typeof res === 'object' && 'succeeded' in (res as object) && 'data' in (res as object)) {
    return (res as { data: T; succeeded: boolean }).data
  }
  return res as T
}

export function createAuthorizationBridge(deps: AuthorizationBridgeDeps = {}): AuthorizationBridge {
  const fmApi = deps.functionModuleApi ?? (deps.client ? useAdminFunctionModuleApi(deps.client) : null)
  const mfApi = deps.moduleFunctionApi ?? (deps.client ? useAdminModuleFunctionApi(deps.client) : null)
  const rfApi = deps.roleFunctionApi ?? (deps.client ? useAdminRoleFunctionApi(deps.client) : null)
  const erApi = deps.entityRoleApi ?? (deps.client ? useAdminEntityRoleApi(deps.client) : null)

  // When called with no deps (e.g. scaffold test), return a no-op bridge rather than throwing.
  // Production callers MUST provide either `client` or all api deps.
  if (!fmApi || !mfApi || !rfApi || !erApi) {
    const noOp = () => Promise.reject(new Error('createAuthorizationBridge: no deps provided'))
    return {
      functionModules: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never },
      permissions: { fetch: noOp as never },
      roleFunctions: { fetch: noOp as never },
      entityRoles: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never },
    }
  }

  const functionModules: BridgeCrudContract<FunctionModuleDto, CreateFunctionModuleDto, UpdateFunctionModuleDto> = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FunctionModuleDto>> => {
      const items = unwrap<FunctionModuleDto[]>(await fmApi.getList())
      return pageArray(items, query)
    },
    create: async (data) => unwrap(await fmApi.create(data)) as FunctionModuleDto,
    update: async (id, data) => unwrap(await fmApi.update(String(id), data)) as FunctionModuleDto,
    delete: async (ids) => {
      for (const id of ids) {
        await fmApi.delete(String(id))
      }
    },
  }

  // Permissions (ModuleFunction) — read-only; requires `moduleId` filter.
  const permissions = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>> => {
      const moduleId = (query.filters as Record<string, string>)?.moduleId
      if (!moduleId) return { items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 }
      const items = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
  }

  // RoleFunction is a relationship table — no create/update/delete from admin.
  // Fetch uses the canonical GET /admin/role-functions paged endpoint so the
  // page can browse assignments across ALL roles. Role / function / enabled
  // filters flow through the standard filters bag on CrudPageQuery.
  const roleFunctions = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<RoleFunctionDto>> => {
      const filters = (query.filters ?? {}) as Record<string, unknown>
      const orderBy = query.sortField
        ? `${query.sortField}${query.sortOrder === 'desc' ? ' desc' : ''}`
        : undefined
      const params: RoleFunctionQueryDto = {
        pageIndex: query.pageIndex,
        pageSize: query.pageSize,
        orderBy,
        roleId: typeof filters.roleId === 'string' ? filters.roleId : undefined,
        functionId: typeof filters.functionId === 'string' ? filters.functionId : undefined,
        isEnabled: typeof filters.isEnabled === 'boolean' ? filters.isEnabled : undefined,
      }
      const result = unwrap<{ items: RoleFunctionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await rfApi.getPagedList(params),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
  }

  const entityRoles: BridgeCrudContract<EntityRoleDto, CreateEntityRoleDto, UpdateEntityRoleDto> = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<EntityRoleDto>> => {
      const roleId = (query.filters as Record<string, string>)?.roleId
      if (!roleId) return { items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 }
      const items = unwrap<EntityRoleDto[]>(await erApi.getByRole(roleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
    create: async (data) => unwrap(await erApi.create(data)) as EntityRoleDto,
    update: async (id, data) => unwrap(await erApi.update(String(id), data)) as EntityRoleDto,
    delete: async (ids) => {
      for (const id of ids) {
        await erApi.delete(String(id))
      }
    },
  }

  return { functionModules, permissions, roleFunctions, entityRoles }
}
