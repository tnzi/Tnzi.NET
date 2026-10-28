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
 *   - permissions      → /admin/module-functions (catalogue + writes),
 *                        holders via /admin/function-authorization/permission/holders
 *   - roleFunctions    → /admin/role-functions  (fetch by functionId, catalogue, roles)
 *   - userFunctions    → /admin/user-functions  (direct grants, no role; catalogue)
 *   - entityRoles      → /admin/data-auth/entity-roles (fetch by roleId)
 *
 * Each page reads the permission catalogue from the controller whose class-level
 * code gates that page (`roleFunctions.catalogue()` behind
 * `authorization.roleFunction.view`, `userFunctions.catalogue()` behind
 * `authorization.userFunction.view`, `permissions.catalogue()` behind
 * `authorization.permission.view`). Reading it from `/admin/modules` made every
 * one of them also require `authorization.functionModule.view`, so a delegated
 * administrator holding only the page's code got a 403 and an empty matrix.
 */
import {
  useAdminFunctionAuthorizationApi,
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
  type PermissionHoldersDto,
  type PermissionCatalogueModuleDto,
  type AssignableRoleDto,
} from '@tnzi/core/services/authorization'
import type { ApiResult } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, pageArray, pagedResult, unwrapOk } from '../_mappers'

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

/**
 * The permission catalogue as the matrices consume it: the flat module list
 * (hierarchy via `parentId`, disabled and retired rows included) and each
 * module's functions keyed by module id.
 */
export interface PermissionCatalogue {
  modules: FunctionModuleDto[]
  functionsByModule: Map<string, ModuleFunctionDto[]>
}

function toCatalogue(rows: PermissionCatalogueModuleDto[] | null | undefined): PermissionCatalogue {
  const list = Array.isArray(rows) ? rows : []
  return {
    modules: list,
    functionsByModule: new Map(list.map((m) => [m.id, Array.isArray(m.functions) ? m.functions : []])),
  }
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
  functionAuthorizationApi?: ReturnType<typeof useAdminFunctionAuthorizationApi>
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
    /** One module's functions (`filters.moduleId`), read from the catalogue behind `authorization.permission.view`. */
    fetch(query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>>
    /**
     * Direct, unpaginated fetch by module from the module management surface
     * (`authorization.functionModule.view`). Pages gated by another code read
     * their own surface's `catalogue()` instead.
     */
    getByModule(moduleId: string): Promise<ModuleFunctionDto[]>
    /** The whole catalogue behind `authorization.permission.view` (the permission catalogue page). */
    catalogue(): Promise<PermissionCatalogue>
    create(data: CreateModuleFunctionDto): Promise<ModuleFunctionDto>
    update(id: string, data: UpdateModuleFunctionDto): Promise<ModuleFunctionDto>
    delete(ids: string[]): Promise<void>
    enable(id: string): Promise<void>
    disable(id: string): Promise<void>
    /**
     * Who holds a code: roles that grant it (super-admin roles, effective,
     * dormant and disabled grants), the accounts that hold it and through what,
     * and the accounts denied it. Rejects with the server message on failure
     * (404 unknown code, 501 Identity membership unavailable).
     */
    holders(code: string): Promise<PermissionHoldersDto>
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
    /** The catalogue the role matrix renders, behind `authorization.roleFunction.view`. */
    catalogue(): Promise<PermissionCatalogue>
    /** Every role (unpaged) for the list beside the matrix, behind `authorization.roleFunction.view`. */
    roles(): Promise<AssignableRoleDto[]>
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
    /** The catalogue the user grant matrix renders, behind `authorization.userFunction.view`. */
    catalogue(): Promise<PermissionCatalogue>
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
    /** Every role (unpaged) for the matrix's role axis, behind `authorization.entityRole.view`. */
    roles(): Promise<AssignableRoleDto[]>
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

/**
 * A grant set read whole for an editor whose Save writes it back whole (role
 * assignments, a user's direct grants, a user's denies). A refused or malformed
 * read throws: an empty list in its place reads as "holds nothing", and the next
 * Save would replace every grant held, or lift every deny, with what was ticked.
 */
function readGrantIds(result: ApiResult<string[]> | string[]): string[] {
  const ids = unwrapOk<string[] | null>(result)
  if (!Array.isArray(ids)) throw new Error('The server did not return the permission list.')
  return ids
}

/**
 * Every module below `rootId` in the parent hierarchy, breadth-first, each
 * listed once and the root itself excluded. Walks `parentId` links over the
 * flat module list and tracks visited ids, so a parent cycle left in the data
 * (A -> B -> A) terminates instead of growing the queue until the tab hangs.
 * Used by the cascade preview and by the parent picker, which must not offer
 * a module's own descendants (choosing one would create exactly that cycle).
 */
export function collectDescendants<T extends { id: string; parentId?: string | null }>(
  modules: readonly T[],
  rootId: string,
): T[] {
  const childrenByParent = new Map<string, T[]>()
  for (const m of modules) {
    if (!m.parentId) continue
    const siblings = childrenByParent.get(m.parentId) ?? []
    siblings.push(m)
    childrenByParent.set(m.parentId, siblings)
  }
  const visited = new Set<string>([rootId])
  const descendants: T[] = []
  const queue: T[] = [...(childrenByParent.get(rootId) ?? [])]
  while (queue.length > 0) {
    const node = queue.shift() as T
    if (visited.has(node.id)) continue
    visited.add(node.id)
    descendants.push(node)
    queue.push(...(childrenByParent.get(node.id) ?? []))
  }
  return descendants
}

export function createAuthorizationBridge(deps: AuthorizationBridgeDeps = {}): AuthorizationBridge {
  const fmApi = deps.functionModuleApi ?? (deps.client ? useAdminFunctionModuleApi(deps.client) : null)
  const mfApi = deps.moduleFunctionApi ?? (deps.client ? useAdminModuleFunctionApi(deps.client) : null)
  const rfApi = deps.roleFunctionApi ?? (deps.client ? useAdminRoleFunctionApi(deps.client) : null)
  const ufApi = deps.userFunctionApi ?? (deps.client ? useAdminUserFunctionApi(deps.client) : null)
  const eiApi = deps.entityInfoApi ?? (deps.client ? useAdminEntityInfoApi(deps.client) : null)
  const erApi = deps.entityRoleApi ?? (deps.client ? useAdminEntityRoleApi(deps.client) : null)
  const faApi = deps.functionAuthorizationApi ?? (deps.client ? useAdminFunctionAuthorizationApi(deps.client) : null)

  // When called with no deps (e.g. scaffold test), return a no-op bridge rather than throwing.
  // Production callers MUST provide either `client` or all api deps.
  // (userFunctionApi / entityInfoApi / functionAuthorizationApi are later
  // additions guarded at the call site instead, so tests injecting the original
  // api set keep working.)
  if (!fmApi || !mfApi || !rfApi || !erApi) {
    const noOp = () => Promise.reject(new Error('createAuthorizationBridge: no deps provided'))
    return {
      functionModules: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getAll: noOp as never, enable: noOp as never, disable: noOp as never, previewCascade: noOp as never },
      permissions: { fetch: noOp as never, getByModule: noOp as never, catalogue: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, enable: noOp as never, disable: noOp as never, holders: noOp as never },
      roleFunctions: { fetch: noOp as never, getAssignedIds: noOp as never, setForRole: noOp as never, clearForRole: noOp as never, compare: noOp as never, clone: noOp as never, superAdminRoles: noOp as never, catalogue: noOp as never, roles: noOp as never },
      userFunctions: { getAssignedIds: noOp as never, setForUser: noOp as never, clearForUser: noOp as never, getDeniedIds: noOp as never, setDeniedForUser: noOp as never, catalogue: noOp as never },
      entityInfos: { getAll: noOp as never },
      entityRoles: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never, getByEntityInfo: noOp as never, roles: noOp as never, getForUser: noOp as never, setCell: noOp as never },
    }
  }

  const functionModules: AuthorizationBridge['functionModules'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FunctionModuleDto>> => {
      const items = unwrapOk<FunctionModuleDto[]>(await fmApi.getList())
      return pageArray(Array.isArray(items) ? items : [], query, (m) => [m.name, m.code, m.description])
    },
    create: async (data) => unwrapOk(await fmApi.create(data)) as FunctionModuleDto,
    update: async (id, data) => unwrapOk(await fmApi.update(String(id), data)) as FunctionModuleDto,
    delete: async (ids) => {
      for (const id of ids) {
        ensureOk(await fmApi.delete(String(id)))
      }
    },
    getAll: async (): Promise<FunctionModuleDto[]> =>
      unwrapOk<FunctionModuleDto[]>(await fmApi.getList()) ?? [],
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
      const all = unwrapOk<FunctionModuleDto[]>(await fmApi.getList()) ?? []
      const root = all.find((m) => m.id === id)
      if (!root) {
        throw new Error(`previewCascade: module ${id} not found`)
      }
      const descendants = collectDescendants(all, root.id)
      // Sum function counts across root + descendants. Run in parallel; a
      // 5-node module tree shouldn't trigger more than a handful of calls.
      const moduleIds = [root.id, ...descendants.map((m) => m.id)]
      const counts = await Promise.all(
        moduleIds.map(async (mid) => {
          // A module whose functions cannot be read must not count as having none:
          // the preview is the confirmation of a cascade disable.
          const fns = unwrapOk<ModuleFunctionDto[]>(await mfApi.getByModule(mid))
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

  // Permissions (ModuleFunction) - list requires a `moduleId` filter and reads
  // the catalogue behind the permission page's own code; writes go to the
  // dedicated /admin/module-functions endpoints.
  const permissions: AuthorizationBridge['permissions'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ModuleFunctionDto>> => {
      const moduleId = (query.filters as Record<string, string>)?.moduleId
      if (!moduleId) return pagedResult({ items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 })
      const { functionsByModule } = toCatalogue(unwrapOk<PermissionCatalogueModuleDto[]>(await mfApi.getCatalogue()))
      return pageArray(functionsByModule.get(moduleId) ?? [], query, (f) => [f.name, f.code, f.description])
    },
    getByModule: async (moduleId: string): Promise<ModuleFunctionDto[]> => {
      const items = unwrapOk<ModuleFunctionDto[]>(await mfApi.getByModule(moduleId))
      return Array.isArray(items) ? items : []
    },
    catalogue: async () => toCatalogue(unwrapOk<PermissionCatalogueModuleDto[]>(await mfApi.getCatalogue())),
    create: async (data) => unwrapOk(await mfApi.create(data)) as ModuleFunctionDto,
    update: async (id, data) => unwrapOk(await mfApi.update(id, data)) as ModuleFunctionDto,
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
    holders: async (code) => {
      if (!faApi) throw new Error('createAuthorizationBridge: functionAuthorizationApi not provided')
      return unwrapOk<PermissionHoldersDto>(await faApi.getPermissionHolders(code)) as PermissionHoldersDto
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
      const result = unwrapOk<{ items: RoleFunctionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await rfApi.getPagedList(params),
      )
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
    },
    getAssignedIds: async (roleId: string): Promise<string[]> =>
      readGrantIds(await rfApi.getRoleFunctionIds(roleId)),
    setForRole: async (roleId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await rfApi.setFunctions(roleId, functionIds))
    },
    clearForRole: async (roleId: string): Promise<void> => {
      ensureOk(await rfApi.clearFunctions(roleId))
    },
    compare: async (roleAId, roleBId) =>
      unwrapOk<PermissionComparisonDto>(
        await rfApi.compareRolePermissions(roleAId, roleBId),
      ),
    clone: async (targetRoleId, sourceRoleId) =>
      unwrapOk<number>(await rfApi.cloneRolePermissions(targetRoleId, sourceRoleId)),
    superAdminRoles: async () =>
      unwrapOk<string[]>(await rfApi.getSuperAdminRoles()) ?? [],
    catalogue: async () => toCatalogue(unwrapOk<PermissionCatalogueModuleDto[]>(await rfApi.getCatalogue())),
    roles: async () => {
      const items = unwrapOk<AssignableRoleDto[]>(await rfApi.getRoles())
      return Array.isArray(items) ? items : []
    },
  }

  const requireUfApi = (): NonNullable<typeof ufApi> => {
    if (!ufApi) throw new Error('createAuthorizationBridge: userFunctionApi not provided')
    return ufApi
  }

  const userFunctions: AuthorizationBridge['userFunctions'] = {
    getAssignedIds: async (userId: string): Promise<string[]> =>
      readGrantIds(await requireUfApi().getUserFunctionIds(userId)),
    setForUser: async (userId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await requireUfApi().setFunctions(userId, functionIds))
    },
    clearForUser: async (userId: string): Promise<void> => {
      ensureOk(await requireUfApi().clearFunctions(userId))
    },
    getDeniedIds: async (userId: string): Promise<string[]> =>
      readGrantIds(await requireUfApi().getDeniedFunctionIds(userId)),
    setDeniedForUser: async (userId: string, functionIds: string[]): Promise<void> => {
      ensureOk(await requireUfApi().setDeniedFunctions(userId, functionIds))
    },
    catalogue: async () => toCatalogue(unwrapOk<PermissionCatalogueModuleDto[]>(await requireUfApi().getCatalogue())),
  }

  const entityInfos: AuthorizationBridge['entityInfos'] = {
    getAll: async () => {
      if (!eiApi) return []
      const items = unwrapOk<EntityInfoDto[]>(await eiApi.getAll())
      return Array.isArray(items) ? items : []
    },
  }

  const entityRoles: AuthorizationBridge['entityRoles'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<EntityRoleDto>> => {
      const roleId = (query.filters as Record<string, string>)?.roleId
      if (!roleId) return pagedResult({ items: [], totalCount: 0, pageIndex: 1, pageSize: query.pageSize ?? 20 })
      const items = unwrapOk<EntityRoleDto[]>(await erApi.getByRole(roleId))
      return pageArray(Array.isArray(items) ? items : [], query)
    },
    create: async (data) => unwrapOk(await erApi.create(data)) as EntityRoleDto,
    update: async (id, data) => unwrapOk(await erApi.update(String(id), data)) as EntityRoleDto,
    delete: async (ids) => {
      for (const id of ids) {
        ensureOk(await erApi.delete(String(id)))
      }
    },
    getByEntityInfo: async (entityInfoId: string) => {
      const items = unwrapOk<EntityRoleDto[]>(await erApi.getByEntityInfo(entityInfoId))
      return Array.isArray(items) ? items : []
    },
    roles: async () => {
      const items = unwrapOk<AssignableRoleDto[]>(await erApi.getRoles())
      return Array.isArray(items) ? items : []
    },
    getForUser: async (userId: string) => {
      const items = unwrapOk<EntityRoleDto[]>(await erApi.getUserEntityRoles(userId))
      return Array.isArray(items) ? items : []
    },
    setCell: async ({ entityInfoId, roleId, operation, enable, filter }) => {
      // Look up the existing row for this (entity, role, operation) tuple. A
      // refused lookup rejects: read as "no row" it turned a toggle-off into a
      // silent no-op the page reported as saved, and a toggle-on into a duplicate.
      const existing = (await entityRoles.getByEntityInfo(entityInfoId)).find(
        (r) => r.roleId === roleId && r.operation === operation,
      )
      if (enable) {
        if (existing) {
          // The update request carries the operation and filter only (a rule is in
          // force for as long as it exists), so only a changed filter is worth a call.
          if (filter !== undefined && existing.filter !== filter) {
            ensureOk(await erApi.update(existing.id, { operation, filter }))
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
