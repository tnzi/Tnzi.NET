/**
 * Authorization Module Types
 * Aligned with Tnzi.NET backend Authorization module entities and DTOs.
 */

// ─── FunctionModule ───────────────────────────────────────────────────────────

export interface FunctionModuleDto {
  id: string
  name: string
  code: string
  description?: string
  order: number
  isEnabled: boolean
  parentId?: string
  /**
   * True when the row is owned by an `IPermissionDefinitionProvider`
   * in code. Admin UI must show a "system" badge and disable the
   * Code / Name / ParentId edit fields. The IsEnabled toggle remains
   * editable so ops can disable a permission without redeploying.
   */
  isSystemManaged?: boolean
  /**
   * Transient, view-only flag stamped by the admin module list endpoint:
   * `true` when the module belongs to the FRAMEWORK built-in catalogue
   * (`identity` / `ai` / `finance` / …), `false`/absent for a consumer
   * application's own permission modules. The permission matrix uses it to
   * list the consumer's own permissions first and to separate the built-in
   * catalogue behind its own section header. Absent on older backends → the
   * matrix falls back to a single, unsectioned list.
   */
  isBuiltIn?: boolean
}

export interface CreateFunctionModuleDto {
  name: string
  code: string
  description?: string
  order?: number
  parentId?: string
}

export interface UpdateFunctionModuleDto {
  name: string
  code: string
  description?: string
  order?: number
  parentId?: string
}

// ─── ModuleFunction (permission within a module) ──────────────────────────────

/**
 * Permission audience classification (mirrors backend `PermissionCategory`).
 * Informational metadata only: assignment UIs render a warning badge on
 * Technical codes (diagnostics, MCP, sandbox, system parameters, …) so
 * operators granting roles can spot ops/dangerous surfaces. It does not
 * drive any implicit grant - all non-super-admin access is explicit.
 */
export enum PermissionCategory {
  Business = 'Business',
  Technical = 'Technical',
}

/**
 * The current user's resolved access profile (mirrors backend
 * `AccessProfileDto`) - the single self-service payload the admin shell
 * needs after login. `isSuperAdmin` is backend-authoritative, replacing the
 * old convention of mirroring `Authorization:SuperAdminRoles` role names in
 * front-end config (which could silently drift).
 */
export interface AccessProfileDto {
  isSuperAdmin: boolean
  permissions: string[]
}

export interface ModuleFunctionDto {
  id: string
  name: string
  code: string
  description?: string
  moduleId: string
  isEnabled: boolean
  order: number
  /**
   * True when this permission point was seeded from an
   * `IPermissionDefinitionProvider`. Admin UI surfaces it as read-only
   * (only IsEnabled is editable on system-managed rows).
   */
  isSystemManaged?: boolean
  /**
   * Business (default) or Technical. Admin UI shows a "technical" badge so
   * operators granting permissions can tell ops surfaces from business ones.
   */
  category?: PermissionCategory
}

/** Create a custom permission point (POST /admin/module-functions). */
export interface CreateModuleFunctionDto {
  name: string
  code: string
  moduleId: string
  description?: string
  order?: number
  category?: PermissionCategory
}

/**
 * Update a permission point (PUT /admin/module-functions/{id}).
 * System-managed rows reject Code/ModuleId changes server-side and always
 * keep their code-declared Category; `category` omitted/null = keep current.
 */
export interface UpdateModuleFunctionDto {
  name: string
  code: string
  moduleId: string
  description?: string
  order?: number
  category?: PermissionCategory | null
}

// ─── RoleFunction ─────────────────────────────────────────────────────────────

export interface RoleFunctionDto {
  id: string
  roleId: string
  functionId: string
  /** Function code (permission name), denormalized from ModuleFunction */
  functionCode: string
  /** Function display name, denormalized from ModuleFunction */
  functionName: string
  /** Module ID the function belongs to, denormalized from ModuleFunction */
  moduleId: string
  isEnabled: boolean
  /** Assignment creation time (ISO 8601 UTC) */
  creationTime: string
}

/**
 * Paged query DTO for the canonical GET /admin/role-functions list.
 * Extends the framework PagedQueryDto with role / function / enabled filters.
 */
export interface RoleFunctionQueryDto {
  pageIndex?: number
  pageSize?: number
  orderBy?: string
  roleId?: string
  functionId?: string
  isEnabled?: boolean
}

// ─── Role permission comparison / clone ──────────────────────────────────────

/**
 * Minimal function info for comparison results (backend `FunctionSummaryDto`).
 */
export interface FunctionSummaryDto {
  /** Function ID. */
  id: string
  /** Function code (permission name). */
  code: string
  /** Function display name. */
  name: string
  /** Module code the function belongs to. */
  moduleCode?: string | null
}

/**
 * Result of `GET /admin/role-functions/compare?roleId1=...&roleId2=...`
 * (backend `PermissionComparisonDto`). The two role ids echo the request
 * order; the three buckets carry the function summaries that are exclusive to
 * role 1, exclusive to role 2, or shared by both.
 */
export interface PermissionComparisonDto {
  roleId1: string
  roleId2: string
  /** Functions owned by role 1 but not role 2. */
  onlyInRole1: FunctionSummaryDto[]
  /** Functions owned by role 2 but not role 1. */
  onlyInRole2: FunctionSummaryDto[]
  /** Functions present in both roles. */
  shared: FunctionSummaryDto[]
}

/** Body of `POST /admin/role-functions/role/{roleId}/clone`. */
export interface CloneRolePermissionsRequest {
  sourceRoleId: string
}

// ─── EntityInfo (data-auth entity registry) ───────────────────────────────────

/** Data-auth EntityInfo — describes a backend entity type that supports row-level authorization. */
export interface EntityInfoDto {
  id: string
  name: string
  typeName: string
  displayName?: string | null
  isDataAuthEnabled: boolean
}

export interface CreateEntityInfoDto {
  name: string
  typeName: string
  displayName?: string | null
  isDataAuthEnabled?: boolean
}

export interface UpdateEntityInfoDto {
  displayName?: string | null
  isDataAuthEnabled?: boolean
}

// ─── EntityRole ───────────────────────────────────────────────────────────────

export type DataAuthOperation = 'Query' | 'Update' | 'Delete' | 'All'

export interface EntityRoleDto {
  id: string
  entityInfoId: string
  roleId: string
  operation: DataAuthOperation
  filter?: string
  isEnabled: boolean
}

export interface CreateEntityRoleDto {
  entityInfoId: string
  roleId: string
  operation: DataAuthOperation
  filter?: string
}

export interface UpdateEntityRoleDto {
  operation: DataAuthOperation
  filter?: string
  isEnabled?: boolean
}
