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
  useAdminEntityInfoApi,
  useAdminEntityRoleApi,
  type FunctionModuleDto,
  type CreateFunctionModuleDto,
  type UpdateFunctionModuleDto,
  type ModuleFunctionDto,
  type RoleFunctionDto,
  type RoleFunctionQueryDto,
  type EntityInfoDto,
  type EntityRoleDto,
  type CreateEntityRoleDto,
  type UpdateEntityRoleDto,
  type DataAuthOperation,
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
  entityInfoApi?: ReturnType<typeof useAdminEntityInfoApi>
  entityRoleApi?: ReturnType<typeof useAdminEntityRoleApi>
}

export interface AuthorizationBridge {
  functionModules: BridgeCrudContract<FunctionModuleDto, CreateFunctionModuleDto, UpdateFunctionModuleDto> & {
    /**
     * Module hierarchy fetched as a flat array (every module includes `parentId`).
     * The Permission Master-Detail page rebuilds the tree client-side from this.
     */
    getAll(): Promise<FunctionModuleDto[]>
  }
  /**
   * Permissions (ModuleFunction) — read-only. Backend has no admin create/update/delete.
   * Requires a `moduleId` filter in the query to fetch permissions for a given module.
   */
  permissions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>>
    /** Direct, unpaginated fetch by module. */
    getByModule(moduleId: string): Promise<ModuleFunctionDto[]>
  }
  roleFunctions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<RoleFunctionDto>>
    /** Function IDs currently assigned to a role (drives the checkbox tree). */
    getAssignedIds(roleId: string): Promise<string[]>
    /** Overwrite the full assignment set for a role (used by Save). */
    setForRole(roleId: string, functionIds: string[]): Promise<void>
    /** Remove every assignment for a role. */
    clearForRole(roleId: string): Promise<void>
  }
  /**
   * Data-auth entity registry (entity types that can be authorized at the row
   * level). Drives the EntityRole matrix page's left selector.
   */
  entityInfos: {
    getAll(): Promise<EntityInfoDto[]>
  }
  entityRoles: BridgeCrudContract<EntityRoleDto, CreateEntityRoleDto, UpdateEntityRoleDto> & {
    /** Every EntityRole assignment for a given entity type. */
    getByEntityInfo(entityInfoId: string): Promise<EntityRoleDto[]>
    /**
     * Toggle a single (entity, role, operation) cell. Behaviour:
     *  - `enable=true`  → create if absent, otherwise update isEnabled=true
     *  - `enable=false` → delete the row if it exists (no-op otherwise)
     * Existing rows are looked up by matching all three keys + filter.
     */
    setCell(args: {
      entityInfoId: string
      roleId: string
      operation: DataAuthOperation
      enable: boolean
      filter?: string
    }): Promise<void>
  }
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
  const eiApi = deps.entityInfoApi ?? (deps.client ? useAdminEntityInfoApi(deps.client) : null)
  const erApi = deps.entityRoleApi ?? (deps.client ? useAdminEntityRoleApi(deps.client) : null)

  // When called with no deps (e.g. scaffold test), return a no-op bridge rather than throwing.
  // Production callers MUST provide either `client` or all api deps.
  if (!fmApi || !mfApi || !rfApi || !erApi) {
    const noOp = () => Promise.reject(new Error('createAuthorizationBridge: no deps provided'))
    return {
      functionModules: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getAll: noOp as never },
      permissions: { fetch: noOp as never, getByModule: noOp as never },
      roleFunctions: { fetch: noOp as never, getAssignedIds: noOp as never, setForRole: noOp as never, clearForRole: noOp as never },
      entityInfos: { getAll: noOp as never },
      entityRoles: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getByEntityInfo: noOp as never, setCell: noOp as never },
    }
  }

  const functionModules: AuthorizationBridge['functionModules'] = {
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
    getAll: async (): Promise<FunctionModuleDto[]> =>
      unwrap<FunctionModuleDto[]>(await fmApi.getList()),
  }

  // Permissions (ModuleFunction) — read-only; requires `moduleId` filter.
  const permissions: AuthorizationBridge['permissions'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>> => {
      const moduleId = (query.filters as Record<string, string>)?.moduleId
      if (!moduleId) return { items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 }
      const items = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
    getByModule: async (moduleId: string): Promise<ModuleFunctionDto[]> => {
      const items = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return Array.isArray(items) ? items : []
    },
  }

  // RoleFunction is a relationship table — no create/update/delete from admin.
  // Fetch uses the canonical GET /admin/role-functions paged endpoint so the
  // page can browse assignments across ALL roles. Role / function / enabled
  // filters flow through the standard filters bag on CrudPageQuery.
  const roleFunctions: AuthorizationBridge['roleFunctions'] = {
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
    getAssignedIds: async (roleId: string): Promise<string[]> => {
      const ids = unwrap<string[]>(await rfApi.getRoleFunctionIds(roleId))
      return Array.isArray(ids) ? ids : []
    },
    setForRole: async (roleId: string, functionIds: string[]): Promise<void> => {
      await rfApi.setFunctions(roleId, functionIds)
    },
    clearForRole: async (roleId: string): Promise<void> => {
      await rfApi.clearFunctions(roleId)
    },
  }

  const entityInfos: AuthorizationBridge['entityInfos'] = {
    getAll: async () => {
      if (!eiApi) return []
      const items = unwrap<EntityInfoDto[]>(await eiApi.getAll())
      return Array.isArray(items) ? items : []
    },
  }

  const entityRoles: AuthorizationBridge['entityRoles'] = {
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
    getByEntityInfo: async (entityInfoId: string) => {
      const items = unwrap<EntityRoleDto[]>(await erApi.getByEntityInfo(entityInfoId))
      return Array.isArray(items) ? items : []
    },
    setCell: async ({ entityInfoId, roleId, operation, enable, filter }) => {
      // Look up the existing row for this (entity, role, operation) tuple.
      const existing = (await entityRoles.getByEntityInfo(entityInfoId)).find(
        (r) => r.roleId === roleId && r.operation === operation,
      )
      if (enable) {
        if (existing) {
          if (!existing.isEnabled || (filter !== undefined && existing.filter !== filter)) {
            await erApi.update(existing.id, { operation, filter, isEnabled: true })
          }
        } else {
          await erApi.create({ entityInfoId, roleId, operation, filter })
        }
      } else if (existing) {
        // Toggle-off currently deletes the row outright — matches the prior CRUD
        // behaviour and keeps the matrix readable (no zombie isEnabled=false
        // rows clogging getByEntityInfo results).
        await erApi.delete(existing.id)
      }
    },
  }

  return { functionModules, permissions, roleFunctions, entityInfos, entityRoles }
}
