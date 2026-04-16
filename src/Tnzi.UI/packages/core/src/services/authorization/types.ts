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
