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
  /**
   * True when no module loaded by this deployment declares the code any more.
   * The row and every grant referencing it are kept, but it grants nothing and
   * is hidden from the assignment tree until the declaring module is loaded
   * again (the commonest cause is a host that has not added the sub-module's
   * `[DependsOn]` after a module split). Render it as a greyed "retired" row
   * rather than offering it for assignment.
   */
  isRetired?: boolean
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

/** Body of `POST /admin/role-functions/batch/assign`: grant the same functions to several roles at once. */
export interface BatchAssignFunctionsRequest {
  roleIds: string[]
  functionIds: string[]
}

/**
 * A role's permissions as a portable document (`GET /admin/role-functions/role/{roleId}/export`).
 * Carries function codes, not ids, so it survives a move between deployments.
 */
export interface RolePermissionExportDto {
  version: string
  exportedAt: Date | string
  sourceRoleId?: string | null
  functionCodes: string[]
}

/** Outcome of `POST /admin/role-functions/role/{roleId}/import`. */
export interface PermissionImportResultDto {
  imported: number
  skipped: number
  /** Codes in the document that no declared function matches; nothing was granted for them. */
  notFound: string[]
}

/**
 * Body of `PUT /admin/user-functions/user/{userId}/set-in-scope` and `/set-denied-in-scope`.
 *
 * `scopeFunctionIds` is the slice this write may touch (the full id list the
 * caller's matrix renders); `functionIds` is the new set inside that slice and
 * must be a subset of it (400 otherwise, naming the ids). Rows outside the
 * slice are left as they are. See docs/modules/authorization.md 有界覆盖.
 */
export interface SetUserFunctionsInScopeRequest {
  scopeFunctionIds: string[]
  functionIds: string[]
}

// ─── EntityInfo (data-auth entity registry) ───────────────────────────────────

/** Data-auth EntityInfo - describes a backend entity type that supports row-level authorization. */
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

// ============================================
// Dual control (four-eyes)
// ============================================

/** Lifecycle of a dual-control request. */
export enum DualControlStatus {
  Pending = 'Pending',
  Approved = 'Approved',
  Rejected = 'Rejected',
  Cancelled = 'Cancelled',
}

/**
 * An action that needs a second person's approval before it can run.
 *
 * Approve produces a permit; the *requester* then comes back and executes.
 * That is why there is no "execute" call here - the framework does not know
 * how any given business action is performed.
 */
export interface DualControlRequestDto {
  id: string;
  /** Business action identifier, e.g. `finance.payrun.void`. */
  operation: string;
  targetId?: string | null;
  /**
   * The parameter snapshot that was approved. Execution compares against it
   * byte for byte - approving "transfer 100" must not authorise "transfer 1M".
   */
  payloadJson?: string | null;
  description?: string | null;
  status: DualControlStatus;
  requesterId: string;
  /**
   * Requester's user name, resolved server-side at read time. Null when the
   * Identity module is not loaded or the user no longer exists.
   *
   * The second pair of eyes has to judge whether *this person* should be doing
   * *this thing* - a column of GUIDs cannot answer that.
   */
  requesterName?: string | null;
  approverId?: string | null;
  /** Deciding user's name, same resolution rules as `requesterName`. */
  approverName?: string | null;
  creationTime: string;
  decidedAt?: string | null;
  decisionComment?: string | null;
  expiresAt: string;
  isConsumed: boolean;
  /** Computed server-side: approved, unused and not yet expired. */
  isUsable: boolean;
}

/** Query filter for dual-control requests. */
export interface DualControlQueryDto {
  pageIndex?: number;
  pageSize?: number;
  operation?: string;
  targetId?: string;
  status?: DualControlStatus;
  requesterId?: string;
  /** Only the ones that can still be used. */
  usableOnly?: boolean;
}

/** Comment carried with an approve/reject. */
export interface DualControlDecisionDto {
  comment?: string;
}

// ─── User permission picture ─────────────────────────────────────────────────

/** One role in a {@link UserPermissionPictureDto}, with the codes it contributes. */
export interface UserPermissionPictureRoleDto {
  id: string;
  name: string;
  /** Codes this role grants (within the picture's scope). */
  granted: string[];
}

/** One catalogue entry in a {@link UserPermissionPictureDto}. */
export interface UserPermissionPictureItemDto {
  id: string;
  code: string;
  name: string;
  moduleId: string;
  moduleCode: string;
  moduleName: string;
  category: PermissionCategory;
}

/**
 * One account's permission picture, read in a single call: the catalogue, the
 * role baseline (per role), the user-level allow / deny overrides and the
 * resolved effective set. Resolution matches the runtime check:
 * `effective = (roleGranted ∪ allowed) − denied`; for a super admin `effective`
 * is the whole (scoped) catalogue and the override rows have no effect.
 *
 * The question an administrator opens the permissions page with is "why, and
 * what do I change", which a flat effective list cannot answer; this payload
 * replaces the four round trips (modules, functions per module, role baseline,
 * user overrides) every consumer used to stitch together on the client.
 */
export interface UserPermissionPictureDto {
  userId: string;
  isSuperAdmin: boolean;
  /** Code prefix the picture was narrowed to; null means the whole catalogue. */
  scope?: string | null;
  roles: UserPermissionPictureRoleDto[];
  /** Live catalogue entries (within scope), in module order then function order. */
  catalogue: UserPermissionPictureItemDto[];
  /** Codes granted through any role (the baseline). */
  roleGranted: string[];
  /** Codes granted to this user directly. */
  allowed: string[];
  /** Codes denied to this user directly, whichever role granted them. */
  denied: string[];
  /** Codes in effect. */
  effective: string[];
}
