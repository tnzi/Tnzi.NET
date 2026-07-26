/**
 * Authorization bridge - full implementation (Phase 3 Task 3.6 + 3.8).
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
 *   - userFunctions    → /admin/user-functions  (direct grants, no role)
 *   - entityRoles      → /admin/data-auth/entity-roles (fetch by roleId)
 */
import {
  useAdminFunctionModuleApi,
  useAdminModuleFunctionApi,
  useAdminRoleFunctionApi,
  useAdminUserFunctionApi,
  useAdminEntityInfoApi,
  useAdminEntityRoleApi,
  type FunctionModuleDto,
  type CreateFunctionModuleDto,
  type UpdateFunctionModuleDto,
  type ModuleFunctionDto,
  type CreateModuleFunctionDto,
  type UpdateModuleFunctionDto,
  type RoleFunctionDto,
  type RoleFunctionQueryDto,
  type EntityInfoDto,
  type EntityRoleDto,
  type CreateEntityRoleDto,
  type UpdateEntityRoleDto,
  type DataAuthOperation,
  type PermissionComparisonDto,
} from '@tnzi/core/services/authorization'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, pageArray, pagedResult, unwrapResult as unwrap } from '../_mappers'

// HttpClient type derived from a factory signature.
type HttpClient = Parameters<typeof useAdminFunctionModuleApi>[0]

/**
 * Result of `functionModules.previewCascade(id)` - what flips when an
 * admin disables a parent module. The UI uses this in the confirm
 * dialog so the user can see "you're about to disable X child modules
 * and Y functions" before clicking through.
 */
export interface ModuleCascadePreview {
  /** The module the cascade originates from. */
  rootModuleId: string
  rootModuleName: string
  /** Direct + transitive descendant module count (exclusive of root). */
  descendantModuleCount: number
  /** Descendant modules (flat, ordered breadth-first). */
  descendantModules: FunctionModuleDto[]
  /** Total function count across `rootModule` + every descendant. */
  affectedFunctionCount: number
}

export interface AuthorizationBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock API objects directly. */
  functionModuleApi?: ReturnType<typeof useAdminFunctionModuleApi>
  moduleFunctionApi?: ReturnType<typeof useAdminModuleFunctionApi>
  roleFunctionApi?: ReturnType<typeof useAdminRoleFunctionApi>
  userFunctionApi?: ReturnType<typeof useAdminUserFunctionApi>
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
    /** Enable a module (cascades to all children + functions). */
    enable(id: string): Promise<void>
    /** Disable a module (cascades to all children + functions). */
    disable(id: string): Promise<void>
    /**
     * Compute the cascade footprint of disabling a module - count of
     * descendant modules + functions that would also flip to disabled.
     * Runs entirely in the bridge (no extra backend call) by walking the
     * cached `getAll()` tree + a single `permissions.getByModule` per
     * descendant. Result is what the confirmation modal needs to display.
     */
    previewCascade(id: string): Promise<ModuleCascadePreview>
  }
  /**
   * Permissions (ModuleFunction) - full management via
   * /admin/module-functions (create/update/delete/enable/disable) plus the
   * per-module read used by the Permissions master-detail page. `fetch`
   * requires a `moduleId` filter. System-managed rows (declared by an
   * `IPermissionDefinitionProvider`) reject code/module changes and delete
   * server-side; enable/disable stays available for emergency ops control.
   */
  permissions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>>
    /** Direct, unpaginated fetch by module. */
    getByModule(moduleId: string): Promise<ModuleFunctionDto[]>
    create(data: CreateModuleFunctionDto): Promise<ModuleFunctionDto>
    update(id: string, data: UpdateModuleFunctionDto): Promise<ModuleFunctionDto>
    delete(ids: string[]): Promise<void>
    enable(id: string): Promise<void>
    disable(id: string): Promise<void>
  }
  roleFunctions: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<RoleFunctionDto>>
    /** Function IDs currently assigned to a role (drives the checkbox tree). */
    getAssignedIds(roleId: string): Promise<string[]>
    /** Overwrite the full assignment set for a role (used by Save). */
    setForRole(roleId: string, functionIds: string[]): Promise<void>
    /** Remove every assignment for a role. */
    clearForRole(roleId: string): Promise<void>
    /**
     * Compare two roles' function sets. Returns the structured diff the
     * RoleFunction "compare with another role" modal renders as three
     * columns (only-in-A / only-in-B / common).
     */
    compare(roleAId: string, roleBId: string): Promise<PermissionComparisonDto>
    /**
     * Copy every assignment from `sourceRoleId` into `targetRoleId`. Does
     * NOT remove existing assignments on the target - pair with
     * `clearForRole(targetRoleId)` for hard reset semantics. Returns the
     * number of new assignments inserted.
     */
    clone(targetRoleId: string, sourceRoleId: string): Promise<number>
    /**
     * Role names configured as super administrators - the assignment page
     * renders those roles read-only (members bypass every check).
     */
    superAdminRoles(): Promise<string[]>
  }
  /**
   * User-direct permission grants and denies (no role involved). Resolution
   * is `(role grants ∪ direct grants) − direct denies`, so this surface can
   * give ONE user extra codes or subtract a role-derived code from just that
   * user. Writes are subject to the backend delegation guard (grantable
   * subset of the grantor's own set, for grants and denies alike).
   */
  userFunctions: {
    /** Function IDs directly granted to a user (drives the user matrix). */
    getAssignedIds(userId: string): Promise<string[]>
    /** Overwrite the user's whole direct-grant set (used by Save). */
    setForUser(userId: string, functionIds: string[]): Promise<void>
    /** Remove every direct grant from a user. */
    clearForUser(userId: string): Promise<void>
    /** Function IDs denied for a user (user-level subtraction). */
    getDeniedIds(userId: string): Promise<string[]>
    /** Overwrite the user's deny set; empty list clears all denies. */
    setDeniedForUser(userId: string, functionIds: string[]): Promise<void>
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
     * Effective entity-roles for a single user - aggregated across every
     * role the user holds. Used by the EntityRole page's "Inspect User"
     * tab to debug "why can/can't user X do operation Y on entity Z?"
     * without the admin having to cross-reference role + role-function
     * pages manually.
     */
    getForUser(userId: string): Promise<EntityRoleDto[]>
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

export function createAuthorizationBridge(deps: AuthorizationBridgeDeps = {}): AuthorizationBridge {
  const fmApi = deps.functionModuleApi ?? (deps.client ? useAdminFunctionModuleApi(deps.client) : null)
  const mfApi = deps.moduleFunctionApi ?? (deps.client ? useAdminModuleFunctionApi(deps.client) : null)
  const rfApi = deps.roleFunctionApi ?? (deps.client ? useAdminRoleFunctionApi(deps.client) : null)
  const ufApi = deps.userFunctionApi ?? (deps.client ? useAdminUserFunctionApi(deps.client) : null)
  const eiApi = deps.entityInfoApi ?? (deps.client ? useAdminEntityInfoApi(deps.client) : null)
  const erApi = deps.entityRoleApi ?? (deps.client ? useAdminEntityRoleApi(deps.client) : null)

  // When called with no deps (e.g. scaffold test), return a no-op bridge rather than throwing.
  // Production callers MUST provide either `client` or all api deps.
  // (userFunctionApi / entityInfoApi are later additions guarded at the call
  // site instead, so tests injecting the original api set keep working.)
  if (!fmApi || !mfApi || !rfApi || !erApi) {
    const noOp = () => Promise.reject(new Error('createAuthorizationBridge: no deps provided'))
    return {
      functionModules: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getAll: noOp as never, enable: noOp as never, disable: noOp as never, previewCascade: noOp as never },
      permissions: { fetch: noOp as never, getByModule: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, enable: noOp as never, disable: noOp as never },
      roleFunctions: { fetch: noOp as never, getAssignedIds: noOp as never, setForRole: noOp as never, clearForRole: noOp as never, compare: noOp as never, clone: noOp as never, superAdminRoles: noOp as never },
      userFunctions: { getAssignedIds: noOp as never, setForUser: noOp as never, clearForUser: noOp as never, getDeniedIds: noOp as never, setDeniedForUser: noOp as never },
      entityInfos: { getAll: noOp as never },
      entityRoles: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getByEntityInfo: noOp as never, getForUser: noOp as never, setCell: noOp as never },
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
        ensureOk(await fmApi.delete(String(id)))
      }
    },
    getAll: async (): Promise<FunctionModuleDto[]> =>
      unwrap<FunctionModuleDto[]>(await fmApi.getList()),
    enable: async (id: string) => {
      ensureOk(await fmApi.enable(String(id)))
    },
    disable: async (id: string) => {
      ensureOk(await fmApi.disable(String(id)))
    },
    previewCascade: async (id: string): Promise<ModuleCascadePreview> => {
      // BFS over the flat module list to gather every descendant of `id`,
      // then sum the function counts (one `getByModule` per node - cheap
      // for typical trees of <100 modules, and avoids a dedicated backend
      // endpoint).
      const all = unwrap<FunctionModuleDto[]>(await fmApi.getList())
      const root = all.find((m) => m.id === id)
      if (!root) {
        throw new Error(`previewCascade: module ${id} not found`)
      }
      const childrenByParent = new Map<string, FunctionModuleDto[]>()
      for (const m of all) {
        if (!m.parentId) continue
        const arr = childrenByParent.get(m.parentId) ?? []
        arr.push(m)
        childrenByParent.set(m.parentId, arr)
      }
      const descendants: FunctionModuleDto[] = []
      const queue: FunctionModuleDto[] = [...(childrenByParent.get(root.id) ?? [])]
      while (queue.length > 0) {
        const node = queue.shift() as FunctionModuleDto
        descendants.push(node)
        const kids = childrenByParent.get(node.id)
        if (kids) queue.push(...kids)
      }
      // Sum function counts across root + descendants. Run in parallel; a
      // 5-node module tree shouldn't trigger more than a handful of calls.
      const moduleIds = [root.id, ...descendants.map((m) => m.id)]
      const counts = await Promise.all(
        moduleIds.map(async (mid) => {
          const fns = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(mid))
          return Array.isArray(fns) ? fns.length : 0
        }),
      )
      const affectedFunctionCount = counts.reduce((a, b) => a + b, 0)
      return {
        rootModuleId: root.id,
        rootModuleName: root.name,
        descendantModuleCount: descendants.length,
        descendantModules: descendants,
        affectedFunctionCount,
      }
    },
  }

  // Permissions (ModuleFunction) - list requires a `moduleId` filter; writes
  // go to the dedicated /admin/module-functions endpoints.
  const permissions: AuthorizationBridge['permissions'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>> => {
      const moduleId = (query.filters as Record<string, string>)?.moduleId
      if (!moduleId) return pagedResult({ items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 })
      const items = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
    getByModule: async (moduleId: string): Promise<ModuleFunctionDto[]> => {
      const items = unwrap<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return Array.isArray(items) ? items : []
    },
    create: async (data) => unwrap(await mfApi.create(data)) as ModuleFunctionDto,
    update: async (id, data) => unwrap(await mfApi.update(id, data)) as ModuleFunctionDto,
    delete: async (ids) => {
      for (const id of ids) {
        ensureOk(await mfApi.delete(id))
      }
    },
    enable: async (id) => {
      ensureOk(await mfApi.enable(id))
    },
    disable: async (id) => {
      ensureOk(await mfApi.disable(id))
    },
  }

  // RoleFunction is a relationship table - no create/update/delete from admin.
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
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    getAssignedIds: async (roleId: string): Promise<string[]> => {
      const ids = unwrap<string[]>(await rfApi.getRoleFunctionIds(roleId))
      return Array.isArray(ids) ? ids : []
    },
    setForRole: async (roleId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await rfApi.setFunctions(roleId, functionIds))
    },
    clearForRole: async (roleId: string): Promise<void> => {
      ensureOk(await rfApi.clearFunctions(roleId))
    },
    compare: async (roleAId, roleBId) =>
      unwrap<PermissionComparisonDto>(
        await rfApi.compareRolePermissions(roleAId, roleBId),
      ),
    clone: async (targetRoleId, sourceRoleId) =>
      unwrap<number>(await rfApi.cloneRolePermissions(targetRoleId, sourceRoleId)),
    superAdminRoles: async () =>
      unwrap<string[]>(await rfApi.getSuperAdminRoles()) ?? [],
  }

  const requireUfApi = (): NonNullable<typeof ufApi> => {
    if (!ufApi) throw new Error('createAuthorizationBridge: userFunctionApi not provided')
    return ufApi
  }

  const userFunctions: AuthorizationBridge['userFunctions'] = {
    getAssignedIds: async (userId: string): Promise<string[]> => {
      const ids = unwrap<string[]>(await requireUfApi().getUserFunctionIds(userId))
      return Array.isArray(ids) ? ids : []
    },
    setForUser: async (userId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await requireUfApi().setFunctions(userId, functionIds))
    },
    clearForUser: async (userId: string): Promise<void> => {
      ensureOk(await requireUfApi().clearFunctions(userId))
    },
    getDeniedIds: async (userId: string): Promise<string[]> => {
      const ids = unwrap<string[]>(await requireUfApi().getDeniedFunctionIds(userId))
      return Array.isArray(ids) ? ids : []
    },
    setDeniedForUser: async (userId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await requireUfApi().setDeniedFunctions(userId, functionIds))
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
      if (!roleId) return pagedResult({ items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 })
      const items = unwrap<EntityRoleDto[]>(await erApi.getByRole(roleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
    create: async (data) => unwrap(await erApi.create(data)) as EntityRoleDto,
    update: async (id, data) => unwrap(await erApi.update(String(id), data)) as EntityRoleDto,
    delete: async (ids) => {
      for (const id of ids) {
        ensureOk(await erApi.delete(String(id)))
      }
    },
    getByEntityInfo: async (entityInfoId: string) => {
      const items = unwrap<EntityRoleDto[]>(await erApi.getByEntityInfo(entityInfoId))
      return Array.isArray(items) ? items : []
    },
    getForUser: async (userId: string) => {
      const items = unwrap<EntityRoleDto[]>(await erApi.getUserEntityRoles(userId))
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
            ensureOk(await erApi.update(existing.id, { operation, filter, isEnabled: true }))
          }
        } else {
          ensureOk(await erApi.create({ entityInfoId, roleId, operation, filter }))
        }
      } else if (existing) {
        // Toggle-off currently deletes the row outright - matches the prior CRUD
        // behaviour and keeps the matrix readable (no zombie isEnabled=false
        // rows clogging getByEntityInfo results).
        ensureOk(await erApi.delete(existing.id))
      }
    },
  }

  return { functionModules, permissions, roleFunctions, userFunctions, entityInfos, entityRoles }
}
