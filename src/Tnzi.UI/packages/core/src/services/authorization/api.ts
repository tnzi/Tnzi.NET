/**
 * Authorization Module API
 * Aligned with Tnzi.NET backend Authorization module controllers:
 *   - /admin/modules         (DefaultModuleAdminController)
 *   - /admin/role-functions  (DefaultRoleFunctionAdminController)
 *   - /admin/data-auth       (DefaultDataAuthAdminController)
 */

import type { HttpClient } from '../../http/http'
import type { PagedList } from '../../types/pagination'
import type {
  FunctionModuleDto,
  CreateFunctionModuleDto,
  UpdateFunctionModuleDto,
  ModuleFunctionDto,
  RoleFunctionDto,
  RoleFunctionQueryDto,
  EntityInfoDto,
  CreateEntityInfoDto,
  UpdateEntityInfoDto,
  EntityRoleDto,
  CreateEntityRoleDto,
  UpdateEntityRoleDto,
  PermissionComparisonDto,
  CloneRolePermissionsRequest,
} from './types'

const MODULES_BASE = '/admin/modules'
const ROLE_FUNCTIONS_BASE = '/admin/role-functions'
const DATA_AUTH_BASE = '/admin/data-auth'

// ─── Module Function (permission) API ────────────────────────────────────────

export function useAdminModuleFunctionApi(client: HttpClient) {
  return {
    /** Get all functions (permissions) for a specific module */
    getByModule: (moduleId: string) =>
      client.get<ModuleFunctionDto[]>(`${MODULES_BASE}/${moduleId}/functions`),
  }
}

// ─── Function Module API ──────────────────────────────────────────────────────

export function useAdminFunctionModuleApi(client: HttpClient) {
  return {
    /** Get all modules as a flat list */
    getList: () => client.get<FunctionModuleDto[]>(MODULES_BASE),

    /** Get module tree */
    getTree: () => client.get<FunctionModuleDto[]>(`${MODULES_BASE}/tree`),

    /** Get module by ID */
    getById: (id: string) => client.get<FunctionModuleDto>(`${MODULES_BASE}/${id}`),

    /** Create a module */
    create: (data: CreateFunctionModuleDto) =>
      client.post<FunctionModuleDto>(MODULES_BASE, data),

    /** Update a module */
    update: (id: string, data: UpdateFunctionModuleDto) =>
      client.put<FunctionModuleDto>(`${MODULES_BASE}/${id}`, data),

    /** Delete a module */
    delete: (id: string) => client.delete<void>(`${MODULES_BASE}/${id}`),

    /** Enable a module (cascades to children) */
    enable: (id: string) => client.put<void>(`${MODULES_BASE}/${id}/enable`),

    /** Disable a module (cascades to children) */
    disable: (id: string) => client.put<void>(`${MODULES_BASE}/${id}/disable`),
  }
}

// ─── Role Function API ────────────────────────────────────────────────────────

export function useAdminRoleFunctionApi(client: HttpClient) {
  return {
    /**
     * Canonical paged list of role-function assignments across all roles.
     * Maps to GET /admin/role-functions with optional role/function/enabled filters.
     */
    getPagedList: (query?: RoleFunctionQueryDto) =>
      client.get<PagedList<RoleFunctionDto>>(ROLE_FUNCTIONS_BASE, { params: query }),

    /** Get all function IDs assigned to a role */
    getRoleFunctionIds: (roleId: string) =>
      client.get<string[]>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/function-ids`),

    /** Assign functions to a role */
    assignFunctions: (roleId: string, functionIds: string[]) =>
      client.post<void>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/assign`, { functionIds }),

    /** Remove functions from a role */
    removeFunctions: (roleId: string, functionIds: string[]) =>
      client.post<void>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/remove`, { functionIds }),

    /** Set (overwrite) all functions for a role */
    setFunctions: (roleId: string, functionIds: string[]) =>
      client.put<void>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/set`, { functionIds }),

    /** Clear all functions from a role */
    clearFunctions: (roleId: string) =>
      client.delete<void>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/clear`),

    /** Get all RoleFunction records for a function */
    getFunctionRoles: (functionId: string) =>
      client.get<RoleFunctionDto[]>(`${ROLE_FUNCTIONS_BASE}/function/${functionId}/roles`),

    /**
     * Compare permissions between two roles.
     * Returns `onlyInA`, `onlyInB`, and `common` function lists — the admin
     * UI uses this to power the "compare with another role" modal.
     */
    compareRolePermissions: (roleAId: string, roleBId: string) =>
      client.get<PermissionComparisonDto>(`${ROLE_FUNCTIONS_BASE}/compare`, {
        params: { roleId1: roleAId, roleId2: roleBId },
      }),

    /**
     * Copy every function assignment from `sourceRoleId` to `roleId`.
     * Existing assignments on the target role are not removed first — pair
     * with `clearFunctions(roleId)` if a hard reset is needed.
     */
    cloneRolePermissions: (roleId: string, sourceRoleId: string) =>
      client.post<number>(`${ROLE_FUNCTIONS_BASE}/role/${roleId}/clone`, {
        sourceRoleId,
      } satisfies CloneRolePermissionsRequest),
  }
}

// ─── Entity Info API (data-auth entity registry) ─────────────────────────────

export function useAdminEntityInfoApi(client: HttpClient) {
  return {
    /** List every registered data-auth entity type. */
    getAll: () =>
      client.get<EntityInfoDto[]>(`${DATA_AUTH_BASE}/entity-infos`),

    /** Lookup by .NET type name (qualified). */
    getByTypeName: (typeName: string) =>
      client.get<EntityInfoDto>(`${DATA_AUTH_BASE}/entity-info/${encodeURIComponent(typeName)}`),

    /** Lookup by id. */
    getById: (id: string) =>
      client.get<EntityInfoDto>(`${DATA_AUTH_BASE}/entity-infos/${id}`),

    create: (data: CreateEntityInfoDto) =>
      client.post<EntityInfoDto>(`${DATA_AUTH_BASE}/entity-infos`, data),

    update: (id: string, data: UpdateEntityInfoDto) =>
      client.put<EntityInfoDto>(`${DATA_AUTH_BASE}/entity-infos/${id}`, data),

    delete: (id: string) =>
      client.delete<void>(`${DATA_AUTH_BASE}/entity-infos/${id}`),
  }
}

// ─── Entity Role API ──────────────────────────────────────────────────────────

export function useAdminEntityRoleApi(client: HttpClient) {
  return {
    /** Get all entity roles for a user */
    getUserEntityRoles: (userId: string) =>
      client.get<EntityRoleDto[]>(`${DATA_AUTH_BASE}/user/${userId}/entity-roles`),

    /** Get entity role by ID */
    getById: (id: string) =>
      client.get<EntityRoleDto>(`${DATA_AUTH_BASE}/entity-roles/${id}`),

    /** Get all entity roles for an entity info */
    getByEntityInfo: (entityInfoId: string) =>
      client.get<EntityRoleDto[]>(`${DATA_AUTH_BASE}/entity-infos/${entityInfoId}/roles`),

    /** Get all entity roles for a role */
    getByRole: (roleId: string) =>
      client.get<EntityRoleDto[]>(`${DATA_AUTH_BASE}/roles/${roleId}/entity-roles`),

    /** Create entity role */
    create: (data: CreateEntityRoleDto) =>
      client.post<EntityRoleDto>(`${DATA_AUTH_BASE}/entity-roles`, data),

    /** Update entity role */
    update: (id: string, data: UpdateEntityRoleDto) =>
      client.put<EntityRoleDto>(`${DATA_AUTH_BASE}/entity-roles/${id}`, data),

    /** Delete entity role */
    delete: (id: string) => client.delete<void>(`${DATA_AUTH_BASE}/entity-roles/${id}`),
  }
}
