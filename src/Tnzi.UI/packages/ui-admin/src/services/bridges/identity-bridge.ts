/**
 * Identity bridge - full PoC (Phase 2b Task 2.28).
 *
 * Wires ui-admin CRUD page contracts to @tnzi/core identity admin APIs.
 * The real `@tnzi/core` identity module exports factory functions that take
 * an HttpClient and return an API object - NOT singleton services. This bridge
 * accepts either a client (and builds the APIs internally) or pre-built API
 * objects (so tests can inject mocks without an HttpClient).
 */
import {
  useAdminUserApi,
  useAdminInvitationApi,
  useInvitationApi,
  useAdminRoleApi,
  useAdminTenantApi,
  useAdminLoginLogApi,
  useAdminOrganizationApi,
  useAdminSessionApi,
  useProfileApi,
  useAuthApi,
  oauthLoginUrl,
  registerPasskey as runPasskeyRegistration,
  withStepUp,
  type StepUpGrantDto,
  type PasskeyCredentialDto,
  type AuthConfigDto,
  type UserListItemDto,
  type CreateInvitationDto,
  type InvitationDto,
  type InvitationPreviewDto,
  type AcceptInvitationDto,
  type AcceptInvitationResultDto,
  type CreateUserDto,
  type UpdateProfileDto,
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
  type OAuthLinkTokenDto,
  type DeactivateAccountDto,
  type PersonalDataExportDto,
  type LoginHistoryQueryDto,
  type SendChangeVerificationCodeDto,
  type ChangeEmailDto,
  type ChangePhoneNumberDto,
  type EnableTwoFactorDto,
  type EnableTotpDto,
  type TotpSetupDto,
  type TwoFactorType,
} from '@tnzi/core/services/identity'
import { createPagedList } from '@tnzi/core'
import type { PagedList } from '@tnzi/core'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, unwrapResult as unwrap, mapQueryToListRequest as mapQuery, unwrapOk } from '../_mappers'

// HttpClient type derived from the factory signature so we don't need a separate import.
type HttpClient = Parameters<typeof useAdminUserApi>[0]

export interface IdentityBridgeDeps {
  /** Production path: provide an HttpClient and the bridge builds all APIs internally. */
  client?: HttpClient
  /**
   * Cookie token delivery (`runtime.auth.cookieDelivery`). `invitationAcceptance.accept`
   * issues a session, so in cookie mode it must carry credentials or a cross-origin
   * SPA drops the refresh cookie the response sets. Every other token-issuing call
   * is wired by `createTnziClient`; this one is built here and has to be told.
   */
  withCredentials?: boolean
  /** Test path: inject mock APIs directly. If provided, `client` is ignored for that API. */
  userApi?: ReturnType<typeof useAdminUserApi>
  roleApi?: ReturnType<typeof useAdminRoleApi>
  tenantApi?: ReturnType<typeof useAdminTenantApi>
  loginLogApi?: ReturnType<typeof useAdminLoginLogApi>
  organizationApi?: ReturnType<typeof useAdminOrganizationApi>
  sessionApi?: ReturnType<typeof useAdminSessionApi>
  profileApi?: ReturnType<typeof useProfileApi>
  authApi?: ReturnType<typeof useAuthApi>
  invitationApi?: ReturnType<typeof useAdminInvitationApi>
  /**
   * Step-up verifier for the self-service `me.*` writes the backend marks
   * `[RequireStepUp]` (two-factor changes, contact-change confirmation,
   * deactivate / delete). Called with the scope the server asked for; resolve
   * a grant to replay the call once, `null` to give up (the original challenge
   * is then rethrown, so the caller sees "not done" rather than a silent no-op).
   *
   * Wired by the User Center shell from a `StepUpPromptController` + the
   * `TStepUpModal` it renders. Without it a challenge surfaces exactly as
   * before: an `HttpError` carrying `IDENTITY_STEP_UP_REQUIRED`.
   */
  stepUp?: (scope: string) => Promise<StepUpGrantDto | null>
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
  /**
   * Invitation-based onboarding: open an account with its roles preset, hand the
   * person a one-time link, let them set their own password.
   *
   * ★ There is no `list` here on purpose. An invitation *is* a user account in the
   * `Pending` state, so "who has not accepted yet" is `users.fetch` with an
   * `invitationState` filter - a second list would only drift from the first.
   */
  invitations: {
    /**
     * Open an account and issue the link.
     *
     * The returned `acceptUrl` is readable only on this response; show it to the
     * admin (copy button) or rely on the app's `UserInvitedEvent` handler to mail it.
     */
    create(data: CreateInvitationDto): Promise<InvitationDto>
    /** Resend. The previous link stops working immediately. */
    resend(userId: string, lifetimeHours?: number): Promise<InvitationDto>
    /** Revoke: deletes the not-yet-accepted account, which invalidates its link. */
    revoke(userId: string): Promise<void>
  }
  /**
   * The invitee's half (`/accept-invitation?token=`). Anonymous: the person
   * holding the link has, by definition, no account to sign in with yet, so
   * this is built straight off the HttpClient like `publicUnsubscribe`.
   */
  invitationAcceptance: {
    /**
     * What the link would activate, masked. `null` for every unusable link
     * (expired, revoked, already accepted, malformed) and for a dead network:
     * one situation to the invitee, and telling them apart would tell a prober
     * which tokens are real.
     */
    preview(token: string): Promise<InvitationPreviewDto | null>
    /**
     * Accept. Rejects on a refused envelope (weak password, dead link) with the
     * server's message; resolves `completed: false` + `remainingSteps` when the
     * app's acceptance handler wants more (the token is kept, the same link
     * works again).
     */
    accept(data: AcceptInvitationDto): Promise<AcceptInvitationResultDto>
  }
  users: BridgeCrudContract<UserListItemDto, CreateUserDto, UpdateUserDto> & {
    /**
     * Full record for ONE user (`GET /admin/users/{id}`). The list projection
     * (`UserListItemDto`) omits the profile fields the user detail page shows,
     * so the page hydrates from here rather than from the loaded page of rows.
     */
    getById(id: string): Promise<UserDto>
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
     * Replace the user's role set. `roleIds` is the canonical target list -
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
    /** Direct admin endpoints - `setRoles` is usually preferred. */
    assignRoles(userId: string, roleIds: string[]): Promise<void>
    removeRoles(userId: string, roleIds: string[]): Promise<void>
    /** Move user under a given organization. */
    assignToOrganization(userId: string, organizationId: string): Promise<void>
    /** Detach user from their current organization. */
    removeFromOrganization(userId: string): Promise<void>
  }
  roles: BridgeCrudContract<RoleDto, CreateRoleDto, UpdateRoleDto> & {
    /** Get all roles (no paging) - used to populate the role-assignment
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
   * Organizations are a hierarchical tree, not a paged list - the surface is
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
     * optional filter - omitted returns sessions across ALL users (sorted by
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
   * Public auth config (`GET /auth/config`, anonymous). Tells the User Center
   * which self-service affordances the backend deployment actually supports -
   * e.g. hide "change phone" when no SMS channel is enabled, hide "change email"
   * when no email channel is enabled, and drive the OAuth account-linking list.
   * Returns `null` on failure so consumers can fail-open (assume all enabled).
   */
  getAuthConfig(): Promise<AuthConfigDto | null>
  /**
   * Deployment-prefix-aware URL for starting an OAuth flow with a given
   * provider (`GET /auth/oauth/{provider}/login`). The endpoint is anonymous
   * and the navigation carries no bearer, so "already signed in" is invisible
   * to it: linking a provider to the current account needs a `linkToken` from
   * `me.issueOAuthLinkToken` - with it the callback links instead of logging
   * in. Returns '' when no client is wired.
   */
  oauthLoginUrl(provider: string, returnUrl?: string, linkToken?: string): string
  /**
   * Current-user self-service section ("me"). Wires the
   * `DefaultUserProfileController` endpoints (`/users/profile/*`) used by
   * the personal-center page. Distinct from `users.*` (admin operating on
   * any user) - every method here operates on the caller, no userId arg.
   */
  me: {
    /** Basic profile (username/email/phone/roles/lastLoginTime/…). */
    getProfile(): Promise<UserDto>
    /** Update name / avatar / displayName fields. */
    updateProfile(data: UpdateProfileDto): Promise<UserDto>
    /** Extended detail (nickname/gender/birthday/bio/address). */
    getDetail(): Promise<UserDetailDto>
    updateDetail(data: CreateUserDetailDto): Promise<UserDetailDto>
    /** Old password + new password. */
    changePassword(data: ChangePasswordDto): Promise<void>
    /** Active sessions for the current user. */
    getSessions(): Promise<UserSessionDto[]>
    revokeSession(sessionId: string): Promise<void>
    /**
     * Sign out other devices. Keeps the calling session unless
     * `includeCurrent` is set - a "sign out everywhere" that also signs you out
     * of the tab you clicked it in is a button people learn not to press.
     */
    revokeAllSessions(includeCurrent?: boolean): Promise<void>
    /** 2FA + linked OAuth accounts. */
    getTwoFactorStatus(): Promise<TwoFactorStatusDto>
    getLinkedAccounts(): Promise<UserLoginDto[]>
    /**
     * One-time token that turns the next OAuth start into a *link* for the
     * signed-in account; pass its `token` as the third argument of
     * `oauthLoginUrl`. Without it the anonymous login flow runs and creates a
     * fresh account when the provider's email differs from ours.
     */
    issueOAuthLinkToken(provider: string): Promise<OAuthLinkTokenDto>
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
    /** Disable 2FA globally, destructively (resets the TOTP key + clears all methods). */
    disableTwoFactor(): Promise<void>
    /** Suspend 2FA (master off) while KEEPING the configured methods for later resume. */
    suspendTwoFactor(): Promise<void>
    /** Resume a suspended 2FA (master on) - previously configured methods take effect again. */
    resumeTwoFactor(): Promise<void>
    /** Generate a TOTP shared key + provisioning URI for QR rendering. */
    getTotpSetup(): Promise<TotpSetupDto>
    /** Enable TOTP after the user enters the verification code from their authenticator app. */
    enableTotp(data: EnableTotpDto): Promise<void>
    /** Disable TOTP only (leaves other 2FA providers untouched). */
    disableTotp(): Promise<void>
    /** Disable a single 2FA method (SMS / email / TOTP); other methods stay on. */
    disableTwoFactorMethod(type: TwoFactorType): Promise<void>
    /** Set the preferred 2FA method (must be an enabled method; shown first at login). */
    setPreferredTwoFactor(type: TwoFactorType): Promise<void>
    // -- Passkeys (WebAuthn) --
    /** The current user's registered passkeys. */
    getPasskeys(): Promise<PasskeyCredentialDto[]>
    /**
     * Run the full registration ceremony for the current user.
     *
     * Returns `null` when the user dismissed the system dialog - that is a normal
     * outcome, not a failure, and must not be surfaced as an error.
     */
    registerPasskey(deviceName?: string): Promise<PasskeyCredentialDto | null>
    removePasskey(credentialId: string): Promise<void>
  }
}

/**
 * Coerce the named filter keys from the string values `'true'` / `'false'`
 * (emitted by the Users / LoginLogs search `NSelect`s, whose options bind
 * string values) into real booleans. The backend query DTOs type these as
 * `bool?`; a JSON string in the POST body fails model binding with a 400.
 * Empty string / null / undefined are dropped so an unset select means "no
 * filter" rather than sending a blank value. Returns a shallow copy - the
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
  // organizationApi + sessionApi are optional - they back the
  // organizations / sessions sub-contracts only. If neither a client
  // nor an explicit mock is supplied, the sub-contract methods reject
  // with a clear error, leaving the rest of the bridge usable. This
  // keeps the existing 4-api test fixtures passing.
  const organizationApi =
    deps.organizationApi ?? (deps.client ? useAdminOrganizationApi(deps.client) : null)
  const sessionApi =
    deps.sessionApi ?? (deps.client ? useAdminSessionApi(deps.client) : null)
  // Self-service profile API for the personal-center page. Optional like
  // organizationApi/sessionApi - if neither a client nor a mock is wired,
  // the `me.*` methods fall back to rejecting promises with a helpful error.
  const profileApi =
    deps.profileApi ?? (deps.client ? useProfileApi(deps.client) : null)
  // authApi backs the anonymous `getAuthConfig()` capability probe. Optional
  // like the others - degrades to a null-returning no-op (fail-open) when
  // neither a client nor a mock is supplied.
  const authApi =
    deps.authApi ?? (deps.client ? useAuthApi(deps.client) : null)

  if (!userApi || !roleApi || !tenantApi || !loginLogApi) {
    throw new Error(
      'createIdentityBridge: provide either `client` (HttpClient) or all four api deps (userApi/roleApi/tenantApi/loginLogApi)',
    )
  }

  // Helper: lazy-reject when an optional sub-api wasn't wired.
  const missing = <T>(label: string): Promise<T> =>
    Promise.reject(new Error(`identity-bridge: ${label} requires an HttpClient or explicit api mock`))

  // Helper: run a `[RequireStepUp]` write through core's challenge -> verify ->
  // replay-once loop when a verifier is wired. `withStepUp` recognises the
  // thrown `HttpError` `ensureOk` produces (it checks `errorCode`, not the
  // status, so an expired session is never mistaken for a challenge). The set
  // of guarded methods mirrors `DefaultUserProfileController`'s attributes and
  // is pinned by `__tests__/services/bridges/identity-bridge-step-up.test.ts`.
  const stepUpGuarded = <T>(run: () => Promise<T>): Promise<T> =>
    deps.stepUp ? withStepUp(run, deps.stepUp) : run()

  const invitationApi =
    deps.invitationApi ?? (deps.client ? useAdminInvitationApi(deps.client) : null)

  const invitations: IdentityBridge['invitations'] = {
    create: async (data) =>
      invitationApi
        ? (unwrapOk(await invitationApi.create(data)) as InvitationDto)
        : missing<InvitationDto>('invitations.create'),
    resend: async (userId, lifetimeHours) =>
      invitationApi
        ? (unwrapOk(await invitationApi.resend(userId, lifetimeHours)) as InvitationDto)
        : missing<InvitationDto>('invitations.resend'),
    revoke: async (userId) => {
      if (!invitationApi) return missing<void>('invitations.revoke')
      ensureOk(await invitationApi.revoke(userId))
    },
  }

  // ★ Anonymous: built straight off the HttpClient, no admin api factory.
  const acceptanceApi = deps.client
    ? useInvitationApi(deps.client, { withCredentials: deps.withCredentials })
    : null
  const invitationAcceptance: IdentityBridge['invitationAcceptance'] = {
    preview: async (token) => {
      if (!acceptanceApi) return null
      try {
        const res = await acceptanceApi.preview(token)
        return res.succeeded ? (res.data ?? null) : null
      } catch {
        return null
      }
    },
    accept: async (data) => {
      if (!acceptanceApi) return missing<AcceptInvitationResultDto>('invitationAcceptance.accept')
      return unwrapOk(await acceptanceApi.accept(data)) as AcceptInvitationResultDto
    },
  }

  const users: IdentityBridge['users'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<UserListItemDto>>(
          await userApi.getList(
            mapQuery(coerceBoolFilters(q, ['isLockedOut', 'isEmailConfirmed'])) as unknown as UserListQueryDto,
          ),
        ),
      ),
    getById: async (id) => unwrap(await userApi.getById(id)) as UserDto,
    create: async (data) => unwrapOk(await userApi.create(data)) as UserListItemDto,
    update: async (id, data) => unwrapOk(await userApi.update(id, data)) as UserListItemDto,
    delete: async (ids) => {
      ensureOk(await userApi.deleteMany(ids))
    },
    export: async (q) =>
      unwrapOk<Blob>(await userApi.exportCsv(mapQuery(q) as unknown as UserListQueryDto)),
    import: async (file) => {
      ensureOk(await userApi.importCsv(file))
    },
    enable: async (id) => {
      ensureOk(await userApi.enable(id))
    },
    disable: async (id, reason) => {
      ensureOk(await userApi.disable(id, reason ?? null))
    },
    lock: async (id, until, reason) => {
      ensureOk(await userApi.lock(id, { lockoutEnd: until ?? null, reason: reason ?? null }))
    },
    unlock: async (id) => {
      ensureOk(await userApi.unlock(id))
    },
    resetPassword: async (id, newPassword) => {
      ensureOk(await userApi.resetPassword(id, { newPassword }))
    },
    assignRoles: async (userId, roleIds) => {
      if (roleIds.length === 0) return
      ensureOk(await userApi.assignRoles(userId, { roleIds }))
    },
    removeRoles: async (userId, roleIds) => {
      if (roleIds.length === 0) return
      ensureOk(await userApi.removeRoles(userId, { roleIds }))
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
      if (toAdd.length > 0) ensureOk(await userApi.assignRoles(userId, { roleIds: toAdd }))
      if (toRemove.length > 0) ensureOk(await userApi.removeRoles(userId, { roleIds: toRemove }))
    },
    assignToOrganization: async (userId, organizationId) => {
      ensureOk(await userApi.assignToOrganization(userId, { organizationId }))
    },
    removeFromOrganization: async (userId) => {
      ensureOk(await userApi.removeFromOrganization(userId))
    },
  }

  const roles: IdentityBridge['roles'] = {
    fetch: async (q) =>
      toCrudResult(
        unwrap<PagedList<RoleDto>>(
          await roleApi.getPagedList(mapQuery(q) as unknown as RoleListQueryDto),
        ),
      ),
    create: async (data) => unwrapOk(await roleApi.create(data)) as RoleDto,
    update: async (id, data) => unwrapOk(await roleApi.update(id, data)) as RoleDto,
    delete: async (ids) => {
      ensureOk(await roleApi.deleteMany(ids))
    },
    // Non-optional read: the matrices spread the result, so a refusal must
    // throw the server's reason rather than unwrap to a `null` that spreads
    // into "null is not iterable".
    getAll: async () => unwrapOk(await roleApi.getAll()) as RoleDto[],
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
    create: async (data) => unwrapOk(await tenantApi.create(data)) as TenantDto,
    update: async (id, data) => unwrapOk(await tenantApi.update(id, data)) as TenantDto,
    delete: async (ids) => {
      // Tenant admin API has no batch delete - loop sequentially.
      for (const id of ids) {
        ensureOk(await tenantApi.delete(id))
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
        create: async (data) => unwrapOk(await organizationApi.create(data)) as OrganizationDto,
        update: async (id, data) =>
          unwrapOk(await organizationApi.update(id, data)) as OrganizationDto,
        delete: async (id) => {
          ensureOk(await organizationApi.delete(id))
        },
        move: async (id, newParentId) => {
          ensureOk(await organizationApi.move(id, { newParentId }))
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
          ensureOk(await sessionApi.revokeSession(sessionId))
        },
        revokeAllForUser: async (userId, excludeSessionId) => {
          ensureOk(await sessionApi.revokeAllSessions(userId, excludeSessionId ?? null))
        },
        cleanExpired: async (inactiveMinutes) =>
          unwrapOk(await sessionApi.cleanExpired(inactiveMinutes)) as number,
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
        updateProfile: async (data) => unwrapOk(await profileApi.update(data)) as UserDto,
        getDetail: async () => unwrap(await profileApi.getDetail()) as UserDetailDto,
        updateDetail: async (data) =>
          unwrapOk(await profileApi.updateDetail(data)) as UserDetailDto,
        changePassword: async (data) => {
          ensureOk(await profileApi.changePassword(data))
        },
        getSessions: async () => unwrap(await profileApi.getSessions()) as UserSessionDto[],
        revokeSession: async (sessionId) => {
          ensureOk(await profileApi.revokeSession(sessionId))
        },
        revokeAllSessions: async (includeCurrent?: boolean) => {
          ensureOk(await profileApi.revokeAllSessions(includeCurrent ?? false))
        },
        getTwoFactorStatus: async () =>
          unwrap(await profileApi.getTwoFactorStatus()) as TwoFactorStatusDto,
        getLinkedAccounts: async () =>
          unwrap(await profileApi.getLinkedAccounts()) as UserLoginDto[],
        // Adds a permanent login method (the linked identity survives a
        // password change and a revoke-all), so the backend gates it with
        // [RequireStepUp] like the 2FA writes below.
        issueOAuthLinkToken: async (provider) =>
          stepUpGuarded(async () =>
            unwrapOk(await profileApi.issueOAuthLinkToken(provider)) as OAuthLinkTokenDto,
          ),
        unlinkAccount: async (provider) => {
          ensureOk(await profileApi.unlinkAccount(provider))
        },
        getLoginHistory: async (params) =>
          unwrap(await profileApi.getLoginHistory(params)) as LoginLogDto[],
        deactivate: async (data) => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.deactivateAccount(data))
          })
        },
        deleteAccount: async () => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.deleteAccount())
          })
        },
        exportPersonalData: async () =>
          unwrapOk(await profileApi.exportPersonalData()) as PersonalDataExportDto,
        sendChangeEmailCode: async (data) => {
          ensureOk(await profileApi.sendChangeEmailCode(data))
        },
        confirmChangeEmail: async (data) => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.confirmChangeEmail(data))
          })
        },
        sendChangePhoneCode: async (data) => {
          ensureOk(await profileApi.sendChangePhoneCode(data))
        },
        confirmChangePhone: async (data) => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.confirmChangePhone(data))
          })
        },
        enableTwoFactor: async (data) =>
          unwrapOk(await profileApi.enableTwoFactor(data)) as string,
        disableTwoFactor: async () => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.disableTwoFactor())
          })
        },
        suspendTwoFactor: async () => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.suspendTwoFactor())
          })
        },
        resumeTwoFactor: async () => {
          ensureOk(await profileApi.resumeTwoFactor())
        },
        // POSTs `two-factor/totp/setup` (resets the authenticator key) despite the
        // `get` name; with TOTP disabled the backend refuses with a 400 that must
        // reach the user, not surface as a TypeError on `.sharedKey`.
        getTotpSetup: async () =>
          stepUpGuarded(async () => unwrapOk<TotpSetupDto>(await profileApi.getTotpSetup())),
        enableTotp: async (data) => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.enableTotp(data))
          })
        },
        disableTotp: async () => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.disableTotp())
          })
        },
        disableTwoFactorMethod: async (type) => {
          await stepUpGuarded(async () => {
            ensureOk(await profileApi.disableTwoFactorMethod({ type }))
          })
        },
        setPreferredTwoFactor: async (type) => {
          ensureOk(await profileApi.setPreferredTwoFactor({ type }))
        },
        // Passkey endpoints live under /auth, so they need authApi rather than
        // profileApi - guarded on their own so a missing authApi cannot take
        // down the 2FA methods above, which do not depend on it.
        getPasskeys: async () =>
          authApi
            ? ((unwrap(await authApi.getPasskeyCredentials()) as PasskeyCredentialDto[] | null) ?? [])
            : missing<PasskeyCredentialDto[]>('me.getPasskeys'),
        // Delegates to @tnzi/core's browser helper: it owns the parse ->
        // navigator.credentials -> serialise ceremony, so no consuming app has
        // to re-implement the same base64url plumbing. It needs the client
        // itself (not an api object) because it drives both request legs.
        registerPasskey: async (deviceName) =>
          deps.client
            ? runPasskeyRegistration(deps.client, { deviceName })
            : missing<PasskeyCredentialDto | null>('me.registerPasskey'),
        removePasskey: async (credentialId) => {
          if (!authApi) return missing<void>('me.removePasskey')
          ensureOk(await authApi.deletePasskeyCredential(credentialId))
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
        revokeAllSessions: (_includeCurrent?: boolean) => missing('me.revokeAllSessions'),
        getTwoFactorStatus: () => missing('me.getTwoFactorStatus'),
        getLinkedAccounts: () => missing('me.getLinkedAccounts'),
        issueOAuthLinkToken: () => missing('me.issueOAuthLinkToken'),
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
        suspendTwoFactor: () => missing('me.suspendTwoFactor'),
        resumeTwoFactor: () => missing('me.resumeTwoFactor'),
        getTotpSetup: () => missing('me.getTotpSetup'),
        enableTotp: () => missing('me.enableTotp'),
        disableTotp: () => missing('me.disableTotp'),
        disableTwoFactorMethod: () => missing('me.disableTwoFactorMethod'),
        setPreferredTwoFactor: () => missing('me.setPreferredTwoFactor'),
        getPasskeys: () => missing('me.getPasskeys'),
        registerPasskey: () => missing('me.registerPasskey'),
        removePasskey: () => missing('me.removePasskey'),
      }

  const getAuthConfig = async (): Promise<AuthConfigDto | null> => {
    if (!authApi) return null
    try {
      return unwrap<AuthConfigDto>(await authApi.getConfig())
    } catch {
      // Fail-open: a missing/failed capability probe must never break the page -
      // the caller treats null as "assume everything is available".
      return null
    }
  }

  const buildOauthLoginUrl = (provider: string, returnUrl?: string, linkToken?: string): string =>
    deps.client ? oauthLoginUrl(deps.client, provider, returnUrl, linkToken) : ''

  return {
    users,
    invitations,
    invitationAcceptance,
    roles,
    tenants,
    organizations,
    sessions,
    loginLogs,
    me,
    getAuthConfig,
    oauthLoginUrl: buildOauthLoginUrl,
  }
}
