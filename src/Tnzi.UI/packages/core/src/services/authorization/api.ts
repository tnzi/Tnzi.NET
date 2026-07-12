/**
 * Authorization Module API
 * Aligned with Tnzi.NET backend Authorization module controllers:
 *   - /admin/modules                 (DefaultModuleAdminController)
 *   - /admin/role-functions          (DefaultRoleFunctionAdminController)
 *   - /admin/user-functions          (DefaultUserFunctionAdminController)
 *   - /admin/data-auth               (DefaultDataAuthAdminController)
 *   - /admin/function-authorization  (DefaultFunctionAuthorizationAdminController)
 */

import type { HttpClient } from '../../http/http'
import type { PagedList } from '../../types/pagination'
import type {
  FunctionModuleDto,
  CreateFunctionModuleDto,
  UpdateFunctionModuleDto,
  ModuleFunctionDto,
  CreateModuleFunctionDto,
  UpdateModuleFunctionDto,
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
  AccessProfileDto,
} from './types'

const MODULES_BASE = '/admin/modules'
const MODULE_FUNCTIONS_BASE = '/admin/module-functions'
const ROLE_FUNCTIONS_BASE = '/admin/role-functions'
const USER_FUNCTIONS_BASE = '/admin/user-functions'
const DATA_AUTH_BASE = '/admin/data-auth'
const FUNCTION_AUTH_BASE = '/admin/function-authorization'

// ─── Module Function (permission) API ────────────────────────────────────────

export function useAdminModuleFunctionApi(client: HttpClient) {
  return {
    /** Get all functions (permissions) for a specific module */
    getByModule: (moduleId: string) =>
      client.get<ModuleFunctionDto[]>(`${MODULES_BASE}/${moduleId}/functions`),

    /** Create a custom permission point */
    create: (data: CreateModuleFunctionDto) =>
      client.post<ModuleFunctionDto>(MODULE_FUNCTIONS_BASE, data),

    /** Update a permission point (system-managed rows: order/name only) */
    update: (id: string, data: UpdateModuleFunctionDto) =>
      client.put<ModuleFunctionDto>(`${MODULE_FUNCTIONS_BASE}/${id}`, data),

    /** Delete a custom permission point (system-managed rows are rejected) */
    delete: (id: string) => client.delete<void>(`${MODULE_FUNCTIONS_BASE}/${id}`),

    /** Re-enable a single permission point */
    enable: (id: string) => client.post<void>(`${MODULE_FUNCTIONS_BASE}/${id}/enable`),

    /** Emergency-disable a single permission point (role grants stop working) */
    disable: (id: string) => client.post<void>(`${MODULE_FUNCTIONS_BASE}/${id}/disable`),
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

    /**
     * Role names configured as super administrators
     * (`Authorization:SuperAdminRoles`). Their members bypass every
     * permission check, so assignment UIs render those roles read-only.
     */
    getSuperAdminRoles: () =>
      client.get<string[]>(`${ROLE_FUNCTIONS_BASE}/super-admin-roles`),
  }
}

// ─── User Function API (direct user grants, no role involved) ────────────────

/**
 * User-direct permission grants — maps to `DefaultUserFunctionAdminController`
 * (`/admin/user-functions`). Complements the role surface: permission
 * resolution is `(role grants ∪ direct grants) − direct denies`, so this
 * surface can give ONE user extra codes OR take a role-derived code away
 * from just that user (user-level wins; super admins are unaffected).
 * All writes are subject to the backend delegation guard (a non-super grantor
 * may only grant/deny codes from their own permission set).
 */
export function useAdminUserFunctionApi(client: HttpClient) {
  return {
    /** Get the functions directly granted to a user */
    getUserFunctions: (userId: string) =>
      client.get<ModuleFunctionDto[]>(`${USER_FUNCTIONS_BASE}/user/${userId}`),

    /** Get all function IDs directly granted to a user */
    getUserFunctionIds: (userId: string) =>
      client.get<string[]>(`${USER_FUNCTIONS_BASE}/user/${userId}/function-ids`),

    /** Directly grant functions to a user (additive; flips existing deny rows) */
    assignFunctions: (userId: string, functionIds: string[]) =>
      client.post<void>(`${USER_FUNCTIONS_BASE}/user/${userId}/assign`, { functionIds }),

    /** Remove direct grants from a user (role-derived permissions unaffected) */
    removeFunctions: (userId: string, functionIds: string[]) =>
      client.post<void>(`${USER_FUNCTIONS_BASE}/user/${userId}/remove`, { functionIds }),

    /** Set (overwrite) the user's whole direct-grant set */
    setFunctions: (userId: string, functionIds: string[]) =>
      client.put<void>(`${USER_FUNCTIONS_BASE}/user/${userId}/set`, { functionIds }),

    /** Clear every direct grant from a user */
    clearFunctions: (userId: string) =>
      client.delete<void>(`${USER_FUNCTIONS_BASE}/user/${userId}/clear`),

    /** Get all function IDs denied for a user (user-level subtraction) */
    getDeniedFunctionIds: (userId: string) =>
      client.get<string[]>(`${USER_FUNCTIONS_BASE}/user/${userId}/denied-function-ids`),

    /**
     * Set (overwrite) the user's deny set — each denied code is removed from
     * the user's effective permissions no matter which role granted it.
     * Pass an empty list to clear all denies.
     */
    setDeniedFunctions: (userId: string, functionIds: string[]) =>
      client.put<void>(`${USER_FUNCTIONS_BASE}/user/${userId}/set-denied`, { functionIds }),
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

// ─── Function Authorization API (current-user permissions, checks) ────────────

/**
 * Function authorization API — maps to `DefaultFunctionAuthorizationAdminController`
 * (`/admin/function-authorization`).
 *
 * `getUserPermissionNames` is the source the admin shell feeds into its auth
 * store to drive permission-filtered menus and route guards. The backend
 * returns the FULL enabled-function catalogue for super-admins, so the client
 * never needs a super-user special case — a super-admin simply receives every
 * code and therefore sees every menu.
 */
export function useAdminFunctionAuthorizationApi(client: HttpClient) {
  return {
    /** Get the flat list of permission codes granted to a user. */
    getUserPermissionNames: (userId: string) =>
      client.get<string[]>(`${FUNCTION_AUTH_BASE}/user/${userId}/permissions`),
    /**
     * Get the current user's access profile: permission codes plus the
     * backend-authoritative super-admin flag. Preferred over
     * `getUserPermissionNames` - no userId needed and no client-side
     * super-admin inference.
     */
    getAccessProfile: () =>
      client.get<AccessProfileDto>(`${FUNCTION_AUTH_BASE}/access-profile`),
    /** Check whether a user holds a specific permission code. */
    checkPermission: (userId: string, permissionName: string) =>
      client.get<boolean>(`${FUNCTION_AUTH_BASE}/check`, {
        params: { userId, permissionName },
      }),
  }
}
