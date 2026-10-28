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
  /**
   * Present only when the backend narrows the super-admin bypass
   * (`Authorization:SuperAdminBypassScope = Platform`). Then a super admin's
   * `permissions` is the platform catalogue plus their explicit grants, and a
   * client must gate on that list instead of on `isSuperAdmin`. Absent means
   * `All`: the bypass covers every code.
   */
  superAdminBypassScope?: SuperAdminBypassScope
}

/**
 * Which codes the backend's super-admin bypass covers
 * (`Authorization:SuperAdminBypassScope`).
 */
export enum SuperAdminBypassScope {
  /** Every code (default). */
  All = 'All',
  /** Only codes in platform-scoped permission groups. */
  Platform = 'Platform',
}

/**
 * Who a permission group belongs to: the platform operator or the
 * organisation using the application. Drives the narrowed super-admin bypass.
 */
export enum PermissionScope {
  Platform = 'Platform',
  Application = 'Application',
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

/**
 * One module of the permission catalogue together with every one of its
 * functions (`GET /admin/role-functions/catalogue`,
 * `/admin/user-functions/catalogue`, `/admin/module-functions/catalogue`).
 *
 * The list is flat: the hierarchy is carried by `parentId` only. Disabled and
 * retired modules and functions are included with their flags, the same rows
 * `GET /admin/modules` plus `GET /admin/modules/{id}/functions` return, so each
 * page decides what to hide (the assignment matrices drop retired functions,
 * the permission catalogue page greys them out).
 *
 * The same read sits behind each page's own class-level code
 * (`authorization.roleFunction.view` / `authorization.userFunction.view` /
 * `authorization.permission.view`), so a delegated administrator holding only
 * that code can use the page without `authorization.functionModule.view`.
 */
export interface PermissionCatalogueModuleDto extends FunctionModuleDto {
  /** No loaded module declares this module any more. */
  isRetired?: boolean
  /**
   * Platform or application group. Sent only when the super-admin bypass is
   * narrowed to platform codes; an admin-created module is `Application`.
   */
  scope?: PermissionScope
  /** Every function of the module, ordered by `order`. */
  functions: ModuleFunctionDto[]
}

/**
 * A role as listed beside the role assignment matrix
 * (`GET /admin/role-functions/roles`, behind `authorization.roleFunction.view`).
 * Same shape as Identity's role list; the role management page itself stays
 * behind Identity's `role.view`.
 */
export interface AssignableRoleDto {
  id: string
  name: string
  normalizedName?: string | null
  description?: string | null
  isSystem: boolean
  isDefault: boolean
  creationTime: Date | string
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

/**
 * Replaces the editable fields of a registration (the type name is fixed).
 * `name` and `isDataAuthEnabled` are required: the server answers 400 without
 * them rather than defaulting to an empty name or a re-enabled switch.
 */
export interface UpdateEntityInfoDto {
  name: string
  displayName?: string | null
  isDataAuthEnabled: boolean
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

/** Backend: UpdateEntityRoleRequest. A rule is enabled while it exists; delete it to switch it off. */
export interface UpdateEntityRoleDto {
  operation: DataAuthOperation
  filter?: string
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

// ─── Permission holders (who holds a code) ───────────────────────────────────

/**
 * How a role relates to one permission code (mirrors backend
 * `PermissionRoleGrantState`, serialized by member name).
 */
export enum PermissionRoleGrantState {
  /** Super-admin role: members bypass every check; no grant row involved. */
  SuperAdmin = 'SuperAdmin',
  /** Enabled grant on a function in effect: members hold the code (unless denied). */
  Effective = 'Effective',
  /**
   * Enabled grant on a disabled or retired function. Grants nothing today and
   * comes back unchanged when the function does.
   */
  Dormant = 'Dormant',
  /** The grant row exists but its own switch is off. */
  GrantDisabled = 'GrantDisabled',
}

/** One role in a {@link PermissionHoldersDto}. */
export interface PermissionHolderRoleDto {
  id: string;
  /** Empty when the role no longer exists. */
  name: string;
  state: PermissionRoleGrantState;
  /** Members inside the caller's tenant scope whose accounts still exist. */
  memberCount: number;
}

/** A role on a holder's path. */
export interface PermissionHolderRoleRefDto {
  id: string;
  name: string;
}

/** One account that holds the code, with how it holds it. */
export interface PermissionHolderUserDto {
  id: string;
  /** Empty when the caller lacks `user.view` or the backend has no Identity user lookup. */
  userName: string;
  /** Holds it as a super admin (bypasses every check; deny rows do not apply). */
  isSuperAdmin: boolean;
  /** Holds it through a direct user grant. */
  isDirect: boolean;
  /** Roles it holds the code through, super-admin roles first. Empty for direct-only holders. */
  roles: PermissionHolderRoleRefDto[];
}

/** An account with a user-level deny row on the code. */
export interface PermissionDeniedUserDto {
  id: string;
  /** Empty when the caller lacks `user.view` or the backend has no Identity user lookup. */
  userName: string;
}

/**
 * Who holds one permission code (`GET /admin/function-authorization/permission/holders`):
 * the roles that grant it, the accounts that end up holding it and through
 * what, and the accounts explicitly denied it.
 *
 * `users` agrees with the runtime check account by account, narrowed to the
 * caller's tenant scope. Super admins are part of it (they hold every code
 * without a grant row), and when the function is not in effect they are all
 * that is left; grants pointing at it are still listed as `Dormant` because
 * they come back the moment the function does.
 */
export interface PermissionHoldersDto {
  functionId: string;
  code: string;
  name: string;
  category: PermissionCategory;
  isEnabled: boolean;
  isRetired: boolean;
  /** Enabled and not retired. When false only super admins hold the code. */
  isInEffect: boolean;
  /**
   * The caller lacks `user.view`, so `users` and `deniedUsers` carry ids but
   * empty user names. The list itself is complete; account names belong to the
   * user-management surface, not to the permission catalogue.
   */
  userNamesHidden: boolean;
  /** Super-admin roles, then effective, dormant and disabled grants; by name within each. */
  roles: PermissionHolderRoleDto[];
  /** Accounts holding the code, by user name. */
  users: PermissionHolderUserDto[];
  /** Accounts with a deny row (super admins excluded: a deny does not apply to them). */
  deniedUsers: PermissionDeniedUserDto[];
}
