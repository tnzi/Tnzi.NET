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
 * Per-function entry in `PermissionComparisonDto`. The flags mark which of
 * the two compared roles owns the permission. A row is `onlyInRoleA` when
 * `inRoleA && !inRoleB`, `onlyInRoleB` when the reverse, and `common` when
 * both are true. The view can derive that without an extra field.
 */
export interface PermissionDifferenceDto {
  functionId: string
  functionCode: string
  functionName: string
  moduleId: string
  moduleName?: string | null
  inRoleA: boolean
  inRoleB: boolean
}

/** Result of `GET /admin/role-functions/compare?roleId1=...&roleId2=...`. */
export interface PermissionComparisonDto {
  roleAId: string
  roleAName: string
  roleBId: string
  roleBName: string
  /** Functions owned by A but not by B. */
  onlyInRoleA: PermissionDifferenceDto[]
  /** Functions owned by B but not by A. */
  onlyInRoleB: PermissionDifferenceDto[]
  /** Functions present in both roles. */
  common: PermissionDifferenceDto[]
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
