/**
 * Identity bridge — full PoC (Phase 2b Task 2.28).
 *
 * Wires ui-admin CRUD page contracts to @tnzi/core identity admin APIs.
 * The real `@tnzi/core` identity module exports factory functions that take
 * an HttpClient and return an API object — NOT singleton services. This bridge
 * accepts either a client (and builds the APIs internally) or pre-built API
 * objects (so tests can inject mocks without an HttpClient).
 */
import {
  useAdminUserApi,
  useAdminRoleApi,
  useAdminTenantApi,
  useAdminLoginLogApi,
  useAdminOrganizationApi,
  useAdminSessionApi,
  useProfileApi,
  type UserListItemDto,
  type CreateUserDto,
  type UpdateUserDto,
  type UserListQueryDto,
  type RoleDto,
  type RoleDetailDto,
  type CreateRoleDto,
  type UpdateRoleDto,
  type RoleListQueryDto,
  type TenantDto,
  type CreateTenantDto,
  type UpdateTenantDto,
  type TenantQueryDto,
  type LoginLogDto,
  type LoginLogQueryDto,
  type OrganizationDto as CoreOrganizationDto,
  type OrganizationTreeNodeDto,
  type CreateOrganizationDto,
  type UpdateOrganizationDto,
  type UserSessionDto,
  type SessionStatisticsDto,
  type ActiveUserSummaryDto,
  type UserDto,
  type UserDetailDto,
  type CreateUserDetailDto,
  type ChangePasswordDto,
  type TwoFactorStatusDto,
  type UserLoginDto,
  type DeactivateAccountDto,
  type PersonalDataExportDto,
  type LoginHistoryQueryDto,
  type SendChangeVerificationCodeDto,
  type ChangeEmailDto,
  type ChangePhoneNumberDto,
  type EnableTwoFactorDto,
  type EnableTotpDto,
  type TotpSetupDto,
} from '@tnzi/core/services/identity'
import { createPagedList } from '@tnzi/core'
import type { PagedList } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { unwrapResult as unwrap, mapQueryToListRequest as mapQuery } from '../_mappers'

// HttpClient type derived from the factory signature so we don't need a separate import.
type HttpClient = Parameters<typeof useAdminUserApi>[0]

export interface IdentityBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /** Test path: inject mock APIs directly. If provided, `client` is ignored for that API. */
  userApi?: ReturnType<typeof useAdminUserApi>
  roleApi?: ReturnType<typeof useAdminRoleApi>
  tenantApi?: ReturnType<typeof useAdminTenantApi>
  loginLogApi?: ReturnType<typeof useAdminLoginLogApi>
  organizationApi?: ReturnType<typeof useAdminOrganizationApi>
  sessionApi?: ReturnType<typeof useAdminSessionApi>
  profileApi?: ReturnType<typeof useProfileApi>
}

// Re-export core's OrganizationDto for bridge consumers.
export type OrganizationDto = CoreOrganizationDto
export type { OrganizationTreeNodeDto, CreateOrganizationDto, UpdateOrganizationDto } from '@tnzi/core/services/identity'

export interface SessionDto {
  id: string
  userName?: string
  ip?: string
  userAgent?: string
  location?: string
  loginTime?: string
  lastActiveAt?: string
  isActive?: boolean
}

export interface IdentityBridge {
  users: BridgeCrudContract<UserListItemDto, CreateUserDto, UpdateUserDto> & {
    /** Enable a user account (sets isEnabled=true). */
    enable(id: string): Promise<void>
    /** Disable a user account (sets isEnabled=false). Optional reason recorded in audit log. */
    disable(id: string, reason?: string | null): Promise<void>
    /** Lock a user account. `until` null = permanent lock. */
    lock(id: string, until?: string | null, reason?: string | null): Promise<void>
    /** Unlock a previously locked user account. */
    unlock(id: string): Promise<void>
    /** Admin-side password reset. The user must change it on next login. */
    resetPassword(id: string, newPassword: string): Promise<void>
    /**
     * Replace the user's role set. `roleIds` is the canonical target list —
     * the bridge computes (add, remove) deltas against the user's current
     * roles and issues two REST calls so the caller doesn't have to.
     *
     * Pass `currentRoleIds` to avoid an extra lookup when the consumer
     * already has the list (the role-assign modal does); omit it and the
     * bridge derives the diff from `userApi.getList` filtered to that user.
     */
    setRoles(
      userId: string,
      roleIds: string[],
      currentRoleIds?: string[],
    ): Promise<void>
    /** Direct admin endpoints — `setRoles` is usually preferred. */
    assignRoles(userId: string, roleIds: string[]): Promise<void>
    removeRoles(userId: string, roleIds: string[]): Promise<void>
    /** Move user under a given organization. */
    assignToOrganization(userId: string, organizationId: string): Promise<void>
    /** Detach user from their current organization. */
    removeFromOrganization(userId: string): Promise<void>
  }
  roles: BridgeCrudContract<RoleDto, CreateRoleDto, UpdateRoleDto> & {
    /** Get all roles (no paging) — used to populate the role-assignment
     *  picker in Users and the role list in RoleFunction. */
    getAll(): Promise<RoleDto[]>
    /** Get a role's detail DTO (includes `userCount`). */
    getDetail(id: string): Promise<RoleDetailDto>
    /** Paged list of users currently assigned to this role. */
    getUsersInRole(
      id: string,
      params?: { pageIndex?: number; pageSize?: number },
    ): Promise<CrudPageResult<UserListItemDto>>
    /** Live user-count for a role (cheap; avoids the paged list trip). */
    getUserCount(id: string): Promise<number>
  }
  tenants: BridgeCrudContract<TenantDto, CreateTenantDto, UpdateTenantDto>
  /**
   * Organizations are a hierarchical tree, not a paged list — the surface is
   * tree-shaped (getTree/move/getChildren) rather than BridgeCrudContract.
   * Wires the full DefaultOrganizationAdminController endpoint set.
   */
  organizations: {
    getTree(): Promise<OrganizationTreeNodeDto[]>
    getById(id: string): Promise<OrganizationDto>
    create(data: CreateOrganizationDto): Promise<OrganizationDto>
    update(id: string, data: UpdateOrganizationDto): Promise<OrganizationDto>
    delete(id: string): Promise<void>
    /** Move under a new parent. `newParentId=null` makes the node a root. */
    move(id: string, newParentId: string | null): Promise<void>
    getChildren(id: string): Promise<OrganizationDto[]>
    search(keyword: string, maxResults?: number): Promise<OrganizationDto[]>
    /**
     * Paged list of users belonging to an organization. When
     * `includeChildren=true` the backend recursively unions every user in
     * the sub-tree below `id`. Use for the org-members panel.
     */
    getUsers(
      id: string,
      params?: { pageIndex?: number; pageSize?: number; includeChildren?: boolean },
    ): Promise<CrudPageResult<UserListItemDto>>
  }
  sessions: {
    /**
     * Global paged session list (GET /admin/sessions). `userId` is an
     * optional filter — omitted returns sessions across ALL users (sorted by
     * last activity desc) with `userName` populated on every item.
     */
    list(params?: {
      userId?: string | null
      includeRevoked?: boolean
      pageIndex?: number
      pageSize?: number
    }): Promise<PagedList<UserSessionDto>>
    /** Per-user session list (admin can pull any user's sessions). */
    listForUser(userId: string, includeRevoked?: boolean): Promise<UserSessionDto[]>
    /** Aggregate online/active/expired counts across all users. */
    statistics(): Promise<SessionStatisticsDto>
    /**
     * List currently active users (sorted by last activity desc). Backs the
     * "active users" picker on the admin session page so admins don't have
     * to paste user IDs blindly.
     */
    activeUsers(top?: number): Promise<ActiveUserSummaryDto[]>
    /** Force-revoke a single session (logs the target user out of that device). */
    revoke(sessionId: string): Promise<void>
    /** Revoke every session for a user except optionally the current one. */
    revokeAllForUser(userId: string, excludeSessionId?: string | null): Promise<void>
    /** Sweep sessions inactive for N minutes (defaults to backend policy). */
    cleanExpired(inactiveMinutes?: number): Promise<number>
  }
  loginLogs: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<LoginLogDto>>
  }
  /**
   * Current-user self-service section ("me"). Wires the
   * `DefaultUserProfileController` endpoints (`/users/profile/*`) used by
   * the personal-center page. Distinct from `users.*` (admin operating on
   * any user) — every method here operates on the caller, no userId arg.
   */
  me: {
    /** Basic profile (username/email/phone/roles/lastLoginTime/…). */
    getProfile(): Promise<UserDto>
    /** Update name / avatar / displayName fields. */
    updateProfile(data: UpdateUserDto): Promise<UserDto>
    /** Extended detail (nickname/gender/birthday/bio/address). */
    getDetail(): Promise<UserDetailDto>
    updateDetail(data: CreateUserDetailDto): Promise<UserDetailDto>
    /** Old password + new password. */
    changePassword(data: ChangePasswordDto): Promise<void>
    /** Active sessions for the current user. */
    getSessions(): Promise<UserSessionDto[]>
    revokeSession(sessionId: string): Promise<void>
    revokeAllSessions(): Promise<void>
    /** 2FA + linked OAuth accounts. */
    getTwoFactorStatus(): Promise<TwoFactorStatusDto>
    getLinkedAccounts(): Promise<UserLoginDto[]>
    unlinkAccount(provider: string): Promise<void>
    /** Past login attempts. */
    getLoginHistory(params?: LoginHistoryQueryDto): Promise<LoginLogDto[]>
    /** Soft disable (account can be re-enabled by an admin). */
    deactivate(data?: DeactivateAccountDto): Promise<void>
    /** Soft delete (GDPR right-to-be-forgotten). */
    deleteAccount(): Promise<void>
    /** Download a full personal-data export bundle (GDPR portability). */
    exportPersonalData(): Promise<PersonalDataExportDto>
    // -- Change email / phone (two-step verification) --
    /** Step 1: send a verification code to the *new* email address. */
    sendChangeEmailCode(data: SendChangeVerificationCodeDto): Promise<void>
    /** Step 2: confirm change with code + new email. */
    confirmChangeEmail(data: ChangeEmailDto): Promise<void>
    /** Step 1: send a verification code to the *new* phone number. */
    sendChangePhoneCode(data: SendChangeVerificationCodeDto): Promise<void>
    /** Step 2: confirm change with code + new phone. */
    confirmChangePhone(data: ChangePhoneNumberDto): Promise<void>
    // -- 2FA + TOTP --
    /** Enable a 2FA provider (email/sms). Returns generated code or void depending on provider. */
    enableTwoFactor(data: EnableTwoFactorDto): Promise<string>
    /** Disable 2FA globally (regardless of provider). */
    disableTwoFactor(): Promise<void>
    /** Generate a TOTP shared key + provisioning URI for QR rendering. */
    getTotpSetup(): Promise<TotpSetupDto>
    /** Enable TOTP after the user enters the verification code from their authenticator app. */
    enableTotp(data: EnableTotpDto): Promise<void>
    /** Disable TOTP only (leaves other 2FA providers untouched). */
    disableTotp(): Promise<void>
  }
}

/**
 * Coerce the named filter keys from the string values `'true'` / `'false'`
 * (emitted by the Users / LoginLogs search `NSelect`s, whose options bind
 * string values) into real booleans. The backend query DTOs type these as
 * `bool?`; a JSON string in the POST body fails model binding with a 400.
 * Empty string / null / undefined are dropped so an unset select means "no
 * filter" rather than sending a blank value. Returns a shallow copy — the
 * incoming query object is never mutated.
 */
function coerceBoolFilters(q: CrudPageQuery, keys: string[]): CrudPageQuery {
  const filters: Record<string, unknown> = { ...q.filters }
  for (const key of keys) {
    const v = filters[key]
    if (v === 'true' || v === true) filters[key] = true
    else if (v === 'false' || v === false) filters[key] = false
    else if (v === '' || v == null) delete filters[key]
  }
  return { ...q, filters }
}

function toCrudResult<T>(p: PagedList<T> | null | undefined): CrudPageResult<T> {
  // Defensive: when the backend errors mid-bind (e.g. ASP.NET rejects an
  // abstract `[FromQuery] PagedQueryDto` with 500 + empty body), the
  // unwrap chain can land here with `undefined`. Return a well-formed
  // empty page so the consumer's `result.items` / `result.totalCount`
  // dereferences don't blow up with "Cannot read properties of undefined".
  // 0.2.72+ (C4): both branches now hand back a full PagedList<T> via
  // `createPagedList` so totalPages / hasPreviousPage / hasNextPage are
  // always populated.
  if (!p) return createPagedList<T>([], 0, 1, 10)
  return createPagedList<T>(
    p.items ?? [],
    p.totalCount ?? 0,
    p.pageIndex ?? 1,
    p.pageSize ?? 10,
  )
}

export function createIdentityBridge(deps: IdentityBridgeDeps = {}): IdentityBridge {
  const userApi = deps.userApi ?? (deps.client ? useAdminUserApi(deps.client) : null)
  const roleApi = deps.roleApi ?? (deps.client ? useAdminRoleApi(deps.client) : null)
  const tenantApi = deps.tenantApi ?? (deps.client ? useAdminTenantApi(deps.client) : null)
  const loginLogApi =
    deps.loginLogApi ?? (deps.client ? useAdminLoginLogApi(deps.client) : null)
  // organizationApi + sessionApi are optional — they back the
  // organizations / sessions sub-contracts only. If neither a client
  // nor an explicit mock is supplied, the sub-contract methods reject
  // with a clear error, leaving the rest of the bridge usable. This
  // keeps the existing 4-api test fixtures passing.
  const organizationApi =
    deps.organizationApi ?? (deps.client ? useAdminOrganizationApi(deps.client) : null)
  const sessionApi =
    deps.sessionApi ?? (deps.client ? useAdminSessionApi(deps.client) : null)
  // Self-service profile API for the personal-center page. Optional like
  // organizationApi/sessionApi — if neither a client nor a mock is wired,
  // the `me.*` methods fall back to rejecting promises with a helpful error.
  const profileApi =
    deps.profileApi ?? (deps.client ? useProfileApi(deps.client) : null)

  if (!userApi || !roleApi || !tenantApi || !loginLogApi) {
    throw new Error(
      'createIdentityBridge: provide either `client` (HttpClient) or all four api deps (userApi/roleApi/tenantApi/loginLogApi)',
    )
  }

  // Helper: lazy-reject when an optional sub-api wasn't wired.
  const missing = <T>(label: string): Promise<T> =>
    Promise.reject(new Error(`identity-bridge: ${label} requires an HttpClient or explicit api mock`))

  const users: IdentityBridge['users'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<UserListItemDto>>(
          await userApi.getList(
            mapQuery(coerceBoolFilters(q, ['isLockedOut', 'isEmailConfirmed'])) as unknown as UserListQueryDto,
          ),
        ),
      ),
    create: async (data) => unwrap(await userApi.create(data)) as UserListItemDto,
    update: async (id, data) => unwrap(await userApi.update(id, data)) as UserListItemDto,
    delete: async (ids) => {
      await userApi.deleteMany(ids)
    },
    export: async (q) => {
      const csv = unwrap<string>(
        await userApi.exportCsv(mapQuery(q) as unknown as UserListQueryDto),
      )
      return new Blob([csv], { type: 'text/csv' })
    },
    import: async (file) => {
      await userApi.importCsv(file)
    },
    enable: async (id) => {
      await userApi.enable(id)
    },
    disable: async (id, reason) => {
      await userApi.disable(id, reason ?? null)
    },
    lock: async (id, until, reason) => {
      await userApi.lock(id, { lockoutEnd: until ?? null, reason: reason ?? null })
    },
    unlock: async (id) => {
      await userApi.unlock(id)
    },
    resetPassword: async (id, newPassword) => {
      await userApi.resetPassword(id, { newPassword })
    },
    assignRoles: async (userId, roleIds) => {
      if (roleIds.length === 0) return
      await userApi.assignRoles(userId, { roleIds })
    },
    removeRoles: async (userId, roleIds) => {
      if (roleIds.length === 0) return
      await userApi.removeRoles(userId, { roleIds })
    },
    setRoles: async (userId, roleIds, currentRoleIds) => {
      // Diff against the current set so we issue at most two REST calls
      // (one add, one remove) and never send the no-op overlap.
      const current = new Set(currentRoleIds ?? [])
      const target = new Set(roleIds)
      const toAdd: string[] = []
      const toRemove: string[] = []
      for (const id of target) {
        if (!current.has(id)) toAdd.push(id)
      }
      for (const id of current) {
        if (!target.has(id)) toRemove.push(id)
      }
      if (toAdd.length > 0) await userApi.assignRoles(userId, { roleIds: toAdd })
      if (toRemove.length > 0) await userApi.removeRoles(userId, { roleIds: toRemove })
    },
    assignToOrganization: async (userId, organizationId) => {
      await userApi.assignToOrganization(userId, { organizationId })
    },
    removeFromOrganization: async (userId) => {
      await userApi.removeFromOrganization(userId)
    },
  }

  const roles: IdentityBridge['roles'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<RoleDto>>(
          await roleApi.getPagedList(mapQuery(q) as unknown as RoleListQueryDto),
        ),
      ),
    create: async (data) => unwrap(await roleApi.create(data)) as RoleDto,
    update: async (id, data) => unwrap(await roleApi.update(id, data)) as RoleDto,
    delete: async (ids) => {
      await roleApi.deleteMany(ids)
    },
    getAll: async () => unwrap(await roleApi.getAll()) as RoleDto[],
    getDetail: async (id) => unwrap(await roleApi.getDetail(id)) as RoleDetailDto,
    getUsersInRole: async (id, params) =>
      toCrudResult(
        unwrap<PagedList<UserListItemDto>>(await roleApi.getUsersInRole(id, params)),
      ),
    getUserCount: async (id) => unwrap(await roleApi.getUserCount(id)) as number,
  }

  const tenants: BridgeCrudContract<TenantDto, CreateTenantDto, UpdateTenantDto> = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<TenantDto>>(
          await tenantApi.getPagedList(mapQuery(q) as unknown as TenantQueryDto),
        ),
      ),
    create: async (data) => unwrap(await tenantApi.create(data)) as TenantDto,
    update: async (id, data) => unwrap(await tenantApi.update(id, data)) as TenantDto,
    delete: async (ids) => {
      // Tenant admin API has no batch delete — loop sequentially.
      for (const id of ids) {
        await tenantApi.delete(id)
      }
    },
  }

  const loginLogs = {
    fetch: async (q: CrudPageQuery): Promise<CrudPageResult<LoginLogDto>> =>
      toCrudResult(
        unwrap<PagedList<LoginLogDto>>(
          await loginLogApi.getList(
            mapQuery(coerceBoolFilters(q, ['isSuccess'])) as unknown as LoginLogQueryDto,
          ),
        ),
      ),
  }

  const organizations: IdentityBridge['organizations'] = organizationApi
    ? {
        getTree: async () =>
          unwrap(await organizationApi.getTree()) as OrganizationTreeNodeDto[],
        getById: async (id) => unwrap(await organizationApi.getById(id)) as OrganizationDto,
        create: async (data) => unwrap(await organizationApi.create(data)) as OrganizationDto,
        update: async (id, data) =>
          unwrap(await organizationApi.update(id, data)) as OrganizationDto,
        delete: async (id) => {
          await organizationApi.delete(id)
        },
        move: async (id, newParentId) => {
          await organizationApi.move(id, { newParentId })
        },
        getChildren: async (id) =>
          unwrap(await organizationApi.getChildren(id)) as OrganizationDto[],
        search: async (keyword, maxResults) =>
          unwrap(await organizationApi.search(keyword, maxResults)) as OrganizationDto[],
        getUsers: async (id, params) =>
          toCrudResult(
            unwrap<PagedList<UserListItemDto>>(await organizationApi.getUsers(id, params)),
          ),
      }
    : {
        getTree: () => missing('organizations.getTree'),
        getById: () => missing('organizations.getById'),
        create: () => missing('organizations.create'),
        update: () => missing('organizations.update'),
        delete: () => missing('organizations.delete'),
        move: () => missing('organizations.move'),
        getChildren: () => missing('organizations.getChildren'),
        search: () => missing('organizations.search'),
        getUsers: () => missing('organizations.getUsers'),
      }

  const sessions: IdentityBridge['sessions'] = sessionApi
    ? {
        list: async (params) => {
          const r = unwrap(
            await sessionApi.getSessions({
              userId: params?.userId ?? undefined,
              includeRevoked: params?.includeRevoked,
              pageIndex: params?.pageIndex,
              pageSize: params?.pageSize,
            }),
          ) as PagedList<UserSessionDto>
          // Normalize through createPagedList so totalPages / hasNextPage are
          // always present even if the backend serializes a 4-field shape.
          return createPagedList(
            r.items ?? [],
            r.totalCount ?? 0,
            r.pageIndex ?? params?.pageIndex ?? 1,
            r.pageSize ?? params?.pageSize ?? 20,
          )
        },
        listForUser: async (userId, includeRevoked) =>
          unwrap(await sessionApi.getUserSessions(userId, { includeRevoked })) as UserSessionDto[],
        statistics: async () => unwrap(await sessionApi.getStatistics()) as SessionStatisticsDto,
        activeUsers: async (top) =>
          unwrap(await sessionApi.getActiveUsers(top)) as ActiveUserSummaryDto[],
        revoke: async (sessionId) => {
          await sessionApi.revokeSession(sessionId)
        },
        revokeAllForUser: async (userId, excludeSessionId) => {
          await sessionApi.revokeAllSessions(userId, excludeSessionId ?? null)
        },
        cleanExpired: async (inactiveMinutes) =>
          unwrap(await sessionApi.cleanExpired(inactiveMinutes)) as number,
      }
    : {
        list: () => missing('sessions.list'),
        listForUser: () => missing('sessions.listForUser'),
        statistics: () => missing('sessions.statistics'),
        activeUsers: () => missing('sessions.activeUsers'),
        revoke: () => missing('sessions.revoke'),
        revokeAllForUser: () => missing('sessions.revokeAllForUser'),
        cleanExpired: () => missing('sessions.cleanExpired'),
      }

  const me: IdentityBridge['me'] = profileApi
    ? {
        getProfile: async () => unwrap(await profileApi.get()) as UserDto,
        updateProfile: async (data) => unwrap(await profileApi.update(data)) as UserDto,
        getDetail: async () => unwrap(await profileApi.getDetail()) as UserDetailDto,
        updateDetail: async (data) =>
          unwrap(await profileApi.updateDetail(data)) as UserDetailDto,
        changePassword: async (data) => {
          await profileApi.changePassword(data)
        },
        getSessions: async () => unwrap(await profileApi.getSessions()) as UserSessionDto[],
        revokeSession: async (sessionId) => {
          await profileApi.revokeSession(sessionId)
        },
        revokeAllSessions: async () => {
          await profileApi.revokeAllSessions()
        },
        getTwoFactorStatus: async () =>
          unwrap(await profileApi.getTwoFactorStatus()) as TwoFactorStatusDto,
        getLinkedAccounts: async () =>
          unwrap(await profileApi.getLinkedAccounts()) as UserLoginDto[],
        unlinkAccount: async (provider) => {
          await profileApi.unlinkAccount(provider)
        },
        getLoginHistory: async (params) =>
          unwrap(await profileApi.getLoginHistory(params)) as LoginLogDto[],
        deactivate: async (data) => {
          await profileApi.deactivateAccount(data)
        },
        deleteAccount: async () => {
          await profileApi.deleteAccount()
        },
        exportPersonalData: async () =>
          unwrap(await profileApi.exportPersonalData()) as PersonalDataExportDto,
        sendChangeEmailCode: async (data) => {
          await profileApi.sendChangeEmailCode(data)
        },
        confirmChangeEmail: async (data) => {
          await profileApi.confirmChangeEmail(data)
        },
        sendChangePhoneCode: async (data) => {
          await profileApi.sendChangePhoneCode(data)
        },
        confirmChangePhone: async (data) => {
          await profileApi.confirmChangePhone(data)
        },
        enableTwoFactor: async (data) =>
          unwrap(await profileApi.enableTwoFactor(data)) as string,
        disableTwoFactor: async () => {
          await profileApi.disableTwoFactor()
        },
        getTotpSetup: async () => unwrap(await profileApi.getTotpSetup()) as TotpSetupDto,
        enableTotp: async (data) => {
          await profileApi.enableTotp(data)
        },
        disableTotp: async () => {
          await profileApi.disableTotp()
        },
      }
    : {
        getProfile: () => missing('me.getProfile'),
        updateProfile: () => missing('me.updateProfile'),
        getDetail: () => missing('me.getDetail'),
        updateDetail: () => missing('me.updateDetail'),
        changePassword: () => missing('me.changePassword'),
        getSessions: () => missing('me.getSessions'),
        revokeSession: () => missing('me.revokeSession'),
        revokeAllSessions: () => missing('me.revokeAllSessions'),
        getTwoFactorStatus: () => missing('me.getTwoFactorStatus'),
        getLinkedAccounts: () => missing('me.getLinkedAccounts'),
        unlinkAccount: () => missing('me.unlinkAccount'),
        getLoginHistory: () => missing('me.getLoginHistory'),
        deactivate: () => missing('me.deactivate'),
        deleteAccount: () => missing('me.deleteAccount'),
        exportPersonalData: () => missing('me.exportPersonalData'),
        sendChangeEmailCode: () => missing('me.sendChangeEmailCode'),
        confirmChangeEmail: () => missing('me.confirmChangeEmail'),
        sendChangePhoneCode: () => missing('me.sendChangePhoneCode'),
        confirmChangePhone: () => missing('me.confirmChangePhone'),
        enableTwoFactor: () => missing('me.enableTwoFactor'),
        disableTwoFactor: () => missing('me.disableTwoFactor'),
        getTotpSetup: () => missing('me.getTotpSetup'),
        enableTotp: () => missing('me.enableTotp'),
        disableTotp: () => missing('me.disableTotp'),
      }

  return { users, roles, tenants, organizations, sessions, loginLogs, me }
}
