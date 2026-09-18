/**
 * Identity Module API - Auth, User, Role, Organization, Session, LoginLog, Security, Tenant management
 * Aligned with Tnzi.NET backend Identity module controllers
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type { ReorderRequest } from '../../types/api';
import type {
  // Auth
  LoginDto,
  TokenResultDto,
  RegisterDto,
  RefreshTokenDto,
  ForgotPasswordDto,
  ResetPasswordDto,
  ResendEmailConfirmationDto,
  CaptchaDto,
  PasswordStrengthResultDto,
  AuthConfigDto,
  // Quick register
  SendQuickRegisterCodeDto,
  QuickRegisterDto,
  QuickRegisterResultDto,
  SetPasswordDto,
  // Code login
  SendCodeLoginCodeDto,
  CodeLoginDto,
  CodeLoginResultDto,
  // Password recovery
  SendPasswordRecoveryCodeDto,
  ResetPasswordByCodeDto,
  // 2FA
  SendTwoFactorCodeDto,
  TwoFactorChallengeDto,
  VerifyTwoFactorDto,
  PasskeyOptionsDto,
  PasskeyCompleteDto,
  PasskeyCredentialDto,
  PasskeyAssertionBeginDto,
  EnableTwoFactorDto,
  TwoFactorStatusDto,
  TwoFactorMethodRequestDto,
  TotpSetupDto,
  EnableTotpDto,
  // Pending actions
  CompletePasswordChangeDto,
  CompletePendingActionCodeDto,
  PendingActionChallengeDto,
  PendingActionResultDto,
  // Invitation
  CreateInvitationDto,
  InvitationDto,
  InvitationPreviewDto,
  AcceptInvitationDto,
  AcceptInvitationResultDto,
  // User
  UserDto,
  UserListItemDto,
  CreateUserDto,
  UpdateProfileDto,
  UpdateUserDto,
  UserListQueryDto,
  UserStatisticsDto,
  ChangePasswordDto,
  ResetPasswordByAdminDto,
  LockUserDto,
  AssignOrganizationDto,
  AssignRolesDto,
  ConfirmContactDto,
  RemoveRolesDto,
  UpdateUserBatchDto,
  UserImportResult,
  // Profile
  DeactivateAccountDto,
  PersonalDataExportDto,
  UserDetailDto,
  CreateUserDetailDto,
  UserLoginDto,
  OAuthLinkTokenDto,
  ChangeEmailDto,
  ChangePhoneNumberDto,
  SendChangeVerificationCodeDto,
  LoginHistoryQueryDto,
  // Session
  UserSessionDto,
  SessionStatisticsDto,
  ActiveUserSummaryDto,
  // Role
  RoleDto,
  RoleDetailDto,
  CreateRoleDto,
  UpdateRoleDto,
  RoleListQueryDto,
  // Organization
  OrganizationDto,
  OrganizationTreeNodeDto,
  CreateOrganizationDto,
  UpdateOrganizationDto,
  MoveOrganizationDto,
  UpdateSortOrderDto,
  BatchSortOrderItemDto,
  OrganizationStatisticsDto,
  UpdateOrganizationBatchDto,
  // Login log
  LoginLogDto,
  LoginLogQueryDto,
  LoginStatisticsDto,
  UserLoginStatisticsDto,
  LoginTrendItem,
  // Login security
  SecurityOverviewDto,
  UserFailedLoginSummaryDto,
  AbnormalLoginResultDto,
  // Tenant
  TenantDto,
  TenantQueryDto,
  CreateTenantDto,
  UpdateTenantDto,
  // Step-up
  StepUpGrantDto,
  SendStepUpCodeDto,
  StepUpCodeDto,
  StepUpPasskeyDto,
} from './types';

// Route constants aligned with backend controllers
const AUTH_BASE = '/auth';
const PROFILE_BASE = '/users/profile';
const ADMIN_USER_BASE = '/admin/users';
const ADMIN_ROLE_BASE = '/admin/roles';
const ADMIN_ORG_BASE = '/admin/organizations';
const ADMIN_SESSION_BASE = '/admin/sessions';
const ADMIN_LOGIN_LOG_BASE = '/admin/login-logs';
const ADMIN_LOGIN_SECURITY_BASE = '/admin/login-security';
const ADMIN_USER_DETAIL_BASE = '/admin/user-details';
const ADMIN_TENANT_BASE = '/admin/tenants';
const ADMIN_INVITATION_BASE = '/admin/invitations';
const INVITATION_BASE = '/invitations';

// ============================================
// Auth API (DefaultAuthController)
// ============================================

/**
 * Options for {@link useAuthApi}.
 */
export interface AuthApiOptions {
  /**
   * Send cookies on the auth endpoints (`credentials: 'include'`).
   *
   * Required when the backend runs `Identity:TokenDelivery:Mode = Cookie` AND the
   * SPA is on a different origin than the API - without it the browser neither
   * stores the `Set-Cookie` from login nor sends it back on refresh.
   *
   * Defaults to `false` and is deliberately NOT on by default: `include` on a
   * cross-origin request requires the server to answer with
   * `Access-Control-Allow-Credentials` and a concrete origin, so turning it on
   * unconditionally would break every deployment that runs CORS with
   * `AllowAnyOrigin`.
   */
  withCredentials?: boolean;
}

/**
 * Request options for an endpoint that sets, reads or clears the refresh-token
 * cookie. Every endpoint whose response can carry a `TokenResultDto` must use
 * this - in cookie mode the refresh token travels as `Set-Cookie`, and a
 * cross-origin fetch without `credentials: 'include'` makes the browser drop
 * it on the floor: sign-in looks fine, and the first refresh a few minutes
 * later says "session expired".
 */
function cookieAwareOptions(options: AuthApiOptions) {
  return options.withCredentials
    ? { skipAuthRefresh: true, withCredentials: true }
    : { skipAuthRefresh: true };
}

export function useAuthApi(client: HttpClient, options: AuthApiOptions = {}) {
  const cookieAware = cookieAwareOptions(options);

  return {
    /**
     * Get public auth config (which login methods / register / recovery /
     * third-party providers are enabled). Anonymous - used by the login page
     * to render itself per backend deployment config.
     */
    getConfig: () =>
      client.get<AuthConfigDto>(`${AUTH_BASE}/config`),

    // Auth-flow endpoints are marked `skipAuthRefresh`: a 401 from them means
    // "bad credentials" or "session already dead", so triggering the client's
    // token-refresh-and-retry (or the onUnauthorized session-expired handler)
    // would be wrong. For refresh/logout it is also what prevents the calls
    // issued DURING a refresh cycle from re-entering the refresh mutex and
    // stalling every queued request until the refresh timeout.

    /** Login (returns JWT token string) */
    login: (data: LoginDto) =>
      client.post<string>(`${AUTH_BASE}/login`, data, { skipAuthRefresh: true }),

    /** Login with refresh token */
    loginWithRefreshToken: (data: LoginDto) =>
      client.post<TokenResultDto>(`${AUTH_BASE}/login-with-refresh-token`, data, cookieAware),

    /** Refresh token */
    refreshToken: (data: RefreshTokenDto) =>
      client.post<TokenResultDto>(`${AUTH_BASE}/refresh-token`, data, cookieAware),

    /** Register (returns token result) */
    register: (data: RegisterDto) =>
      client.post<TokenResultDto>(`${AUTH_BASE}/register`, data, cookieAware),

    /** Logout (requires authentication) */
    logout: () =>
      client.post<string>(`${AUTH_BASE}/logout`, undefined, cookieAware),

    /** Forgot password (sends reset email) */
    forgotPassword: (data: ForgotPasswordDto) =>
      client.post<string>(`${AUTH_BASE}/forgot-password`, data),

    /**
     * Read what the sign-in is asking for, plus the material to do it.
     *
     * Call this with the `tempToken` from a 403 `IDENTITY_PENDING_ACTIONS_REQUIRED`
     * response. Does not consume the token. Anonymous: whoever gets here does not
     * have an access token yet.
     */
    describePendingActions: (tempToken: string) =>
      client.get<PendingActionChallengeDto>(
        `${AUTH_BASE}/pending-actions/${encodeURIComponent(tempToken)}`,
      ),

    // The three `completePending*` calls issue the session (`PendingActionResultDto.token`),
    // so they are cookie-aware like login. Anonymous otherwise: a 401 here means
    // the temp token is bad, not that a session expired.

    /** Discharge "must change the password first". */
    completePendingPasswordChange: (data: CompletePasswordChangeDto) =>
      client.post<PendingActionResultDto>(`${AUTH_BASE}/pending-actions/change-password`, data, cookieAware),

    /** Discharge "must enrol an authenticator first". */
    completePendingTotpEnrollment: (data: CompletePendingActionCodeDto) =>
      client.post<PendingActionResultDto>(`${AUTH_BASE}/pending-actions/enroll-totp`, data, cookieAware),

    /** Send the code for "must confirm the email first". The address comes from the account. */
    sendPendingActionEmailCode: (tempToken: string) =>
      client.post<void>(
        `${AUTH_BASE}/pending-actions/${encodeURIComponent(tempToken)}/send-email-code`,
      ),

    /** Discharge "must confirm the email first". */
    completePendingEmailConfirmation: (data: CompletePendingActionCodeDto) =>
      client.post<PendingActionResultDto>(`${AUTH_BASE}/pending-actions/confirm-email`, data, cookieAware),

    /** Reset password by token */
    resetPassword: (data: ResetPasswordDto) =>
      client.post<string>(`${AUTH_BASE}/reset-password`, data),

    /** Resend email confirmation */
    resendEmailConfirmation: (data: ResendEmailConfirmationDto) =>
      client.post<string>(`${AUTH_BASE}/resend-email-confirmation`, data),

    /** Get the built-in image captcha (binary PNG, id in the X-Captcha-Id header). Any valid purpose name. */
    getCaptcha: (purpose: string) =>
      client.download(`${AUTH_BASE}/captcha/${encodeURIComponent(purpose)}`),

    /** Get the built-in image captcha as JSON (base64). Any valid purpose name (login / register / password-recovery / yours). */
    getCaptchaJson: (purpose: string) =>
      client.get<CaptchaDto>(`${AUTH_BASE}/captcha/${encodeURIComponent(purpose)}/json`),

    /** Evaluate password strength */
    evaluatePasswordStrength: (password: string) =>
      client.post<PasswordStrengthResultDto>(`${AUTH_BASE}/password-strength`, password),

    // -- Quick Register --

    /** Send quick register verification code */
    sendQuickRegisterCode: (data: SendQuickRegisterCodeDto) =>
      client.post<string>(`${AUTH_BASE}/quick-register/send-code`, data),

    /** Quick register (no password) */
    quickRegister: (data: QuickRegisterDto) =>
      client.post<QuickRegisterResultDto>(`${AUTH_BASE}/quick-register`, data),

    /** Set password after quick register */
    setPassword: (data: SetPasswordDto) =>
      client.post<string>(`${AUTH_BASE}/set-password`, data),

    // -- Code Login --

    /** Send code login verification code */
    sendCodeLoginCode: (data: SendCodeLoginCodeDto) =>
      client.post<string>(`${AUTH_BASE}/code-login/send-code`, data),

    /** Code login */
    codeLogin: (data: CodeLoginDto) =>
      client.post<CodeLoginResultDto>(`${AUTH_BASE}/code-login`, data, cookieAware),

    // -- Password Recovery by Code --

    /** Send password recovery code */
    sendPasswordRecoveryCode: (data: SendPasswordRecoveryCodeDto) =>
      client.post<string>(`${AUTH_BASE}/password-recovery/send-code`, data),

    /** Reset password by verification code */
    resetPasswordByCode: (data: ResetPasswordByCodeDto) =>
      client.post<string>(`${AUTH_BASE}/password-recovery/reset`, data),

    // -- Two-Factor Auth --

    /** Send 2FA code */
    sendTwoFactorCode: (data: SendTwoFactorCodeDto) =>
      client.post<TwoFactorChallengeDto>(`${AUTH_BASE}/send-2fa-code`, data),

    /** Verify 2FA and login */
    verifyTwoFactor: (data: VerifyTwoFactorDto) =>
      client.post<TokenResultDto>(`${AUTH_BASE}/verify-2fa`, data, cookieAware),

    // -- Passkey (WebAuthn) --
    // Two-step by nature: begin produces browser options plus an opaque state
    // handle, complete posts the credential back with that handle. The handle is
    // single-use. Prefer the `registerPasskey` / `signInWithPasskey` helpers in
    // `services/identity/passkey` over calling these four directly.

    /**
     * Begin passkey registration.
     *
     * @param enrollmentToken Optional one-time enrollment token. Without it the
     *   credential is attached to the currently signed-in user; with it, to the
     *   user the token points at (which is what lets someone who has no
     *   credentials yet register one).
     */
    beginPasskeyRegistration: (enrollmentToken?: string) =>
      client.post<PasskeyOptionsDto>(
        `${AUTH_BASE}/passkey/register/begin`,
        undefined,
        { params: enrollmentToken ? { enrollmentToken } : undefined },
      ),

    /** Complete passkey registration. */
    completePasskeyRegistration: (data: PasskeyCompleteDto, enrollmentToken?: string) =>
      client.post<PasskeyCredentialDto>(
        `${AUTH_BASE}/passkey/register/complete`,
        data,
        { params: enrollmentToken ? { enrollmentToken } : undefined },
      ),

    /**
     * Begin passkey sign-in. Anonymous.
     *
     * Omit `userName` for a discoverable-credential flow (the user picks the
     * account in the system dialog, so the login page needs no username field).
     * An unknown username still returns options - the endpoint deliberately does
     * not reveal whether an account exists.
     */
    beginPasskeyAssertion: (data?: PasskeyAssertionBeginDto) =>
      client.post<PasskeyOptionsDto>(`${AUTH_BASE}/passkey/assert/begin`, data ?? {}),

    /**
     * Complete passkey sign-in.
     *
     * Issues through the same exit as password login, so this can still come
     * back with `2FA_REQUIRED` when the user has two-factor enabled - continue
     * with the existing 2FA flow in that case.
     */
    completePasskeyAssertion: (data: PasskeyCompleteDto) =>
      client.post<TokenResultDto>(`${AUTH_BASE}/passkey/assert/complete`, data, cookieAware),

    /** List the current user's registered passkeys. */
    getPasskeyCredentials: () =>
      client.get<PasskeyCredentialDto[]>(`${AUTH_BASE}/passkey/credentials`),

    /** Remove one of the current user's passkeys. */
    deletePasskeyCredential: (credentialId: string) =>
      client.delete(`${AUTH_BASE}/passkey/credentials/${encodeURIComponent(credentialId)}`),

    // -- Step-up (re-authentication for one action) --
    // A valid session is not always enough: a few actions (taking an original
    // attachment out, a bulk export, releasing a payment) need proof that the
    // person is here right now. The server answers those endpoints with
    // `IDENTITY_STEP_UP_REQUIRED`; verify here, then retry the original call
    // unchanged. Prefer the `stepUp` helper in `services/identity/step-up`.
    //
    // All three are `skipAuthRefresh`: a failed verification answers 401 (the
    // backend deliberately says the same thing for a wrong code and a foreign
    // passkey). Left to the client's default 401 handling, one mistyped code
    // would refresh the session, automatically re-submit the SAME wrong code
    // (a second failure server-side) and then sign the user out.

    /**
     * Re-authenticate with a passkey.
     *
     * Reuses the ordinary assertion challenge (`beginPasskeyAssertion`) - there
     * is no separate step-up challenge endpoint, because a second parallel
     * challenge flow would be one more piece of state to keep in sync.
     */
    stepUpWithPasskey: (data: StepUpPasskeyDto) =>
      client.post<StepUpGrantDto>(`${AUTH_BASE}/step-up/passkey`, data, { skipAuthRefresh: true }),

    /**
     * Re-authenticate with a two-factor code.
     *
     * Weaker than a passkey (SMS and email codes can both be relayed). Offer it
     * where passkeys are not available, not as the easier of two options.
     */
    stepUpWithCode: (data: StepUpCodeDto) =>
      client.post<StepUpGrantDto>(`${AUTH_BASE}/step-up/code`, data, { skipAuthRefresh: true }),

    /**
     * Send a step-up-only verification code to the signed-in user's confirmed
     * email / phone. Resolves to the masked destination.
     *
     * The code is bound to step-up: it cannot sign anyone in, reset a password
     * or confirm a contact change, and codes issued by those flows cannot
     * complete a step-up either. No address parameter - what has to be proven
     * is that *this account's owner* is here, and letting the caller name the
     * recipient would hand that decision to a possibly-hijacked session.
     */
    stepUpSendCode: (data: SendStepUpCodeDto) =>
      client.post<string | null>(`${AUTH_BASE}/step-up/send-code`, data, { skipAuthRefresh: true }),
  };
}

/**
 * Build the absolute URL that starts a third-party OAuth login flow.
 * The login page navigates the browser here (`window.location.assign(...)`);
 * the backend (`GET /auth/oauth/{provider}/login`) then redirects to the
 * provider's consent screen.
 *
 * @param client HTTP client (supplies the configured baseUrl).
 * @param provider Provider key (lowercase, e.g. `'github'`, `'google'`).
 * @param returnUrl Optional URL to return to once the flow completes.
 * @param linkToken Optional link token from `useProfileApi().issueOAuthLinkToken`.
 *   With it the flow *links* the provider to the signed-in account instead of
 *   logging in: the callback issues no tokens and creates no account. Without
 *   it, an already-signed-in user hitting this URL runs the anonymous login
 *   flow, which creates a fresh account when the provider's email differs.
 */
export function oauthLoginUrl(
  client: HttpClient,
  provider: string,
  returnUrl?: string,
  linkToken?: string,
): string {
  const query: Record<string, string> = {};
  if (returnUrl) query.returnUrl = returnUrl;
  if (linkToken) query.linkToken = linkToken;
  return client.resolveUrl(
    `${AUTH_BASE}/oauth/${provider}/login`,
    Object.keys(query).length ? query : undefined,
  );
}

// ============================================
// User Profile API (DefaultUserProfileController)
// ============================================

export function useProfileApi(client: HttpClient) {
  return {
    /** Get current user profile */
    get: () =>
      client.get<UserDto>(PROFILE_BASE),

    /** Update profile (self) — privileged fields are not part of this shape */
    update: (data: UpdateProfileDto) =>
      client.put<UserDto>(PROFILE_BASE, data),

    /** Change password */
    changePassword: (data: ChangePasswordDto) =>
      client.post<void>(`${PROFILE_BASE}/change-password`, data),

    // -- Sessions --

    /** Get current user sessions */
    getSessions: () =>
      client.get<UserSessionDto[]>(`${PROFILE_BASE}/sessions`),

    /** Revoke a specific session */
    revokeSession: (sessionId: string) =>
      client.delete<void>(`${PROFILE_BASE}/sessions/${sessionId}`),

    /**
     * Sign out other devices.
     *
     * Defaults to keeping the session that issued this request - that is what
     * ASVS 7.4.3 asks for ("terminate all *other* active sessions"), and a button
     * that also signs you out of the tab you clicked it in is a button nobody
     * presses. Pass `includeCurrent: true` for a true "sign out everywhere".
     */
    revokeAllSessions: (includeCurrent = false) =>
      client.delete<void>(
        `${PROFILE_BASE}/sessions${includeCurrent ? '?includeCurrent=true' : ''}`),

    // -- Linked Accounts --

    /** Get linked OAuth accounts */
    getLinkedAccounts: () =>
      client.get<UserLoginDto[]>(`${PROFILE_BASE}/linked-accounts`),

    /**
     * Issue a one-time link token, then navigate to
     * `oauthLoginUrl(client, provider, returnUrl, token.token)` to link the
     * provider to the current account. See {@link OAuthLinkTokenDto}.
     */
    issueOAuthLinkToken: (provider: string) =>
      client.post<OAuthLinkTokenDto>(`${PROFILE_BASE}/linked-accounts/${encodeURIComponent(provider)}/link-token`),

    /** Unlink an OAuth account */
    unlinkAccount: (provider: string) =>
      client.delete<void>(`${PROFILE_BASE}/linked-accounts/${encodeURIComponent(provider)}`),

    // -- Two-Factor Auth --

    /** Get 2FA status */
    getTwoFactorStatus: () =>
      client.get<TwoFactorStatusDto>(`${PROFILE_BASE}/two-factor/status`),

    /** Enable 2FA */
    enableTwoFactor: (data: EnableTwoFactorDto) =>
      client.post<string>(`${PROFILE_BASE}/two-factor/enable`, data),

    /** Disable 2FA (destructive: resets the TOTP key + clears every method). */
    disableTwoFactor: () =>
      client.post<void>(`${PROFILE_BASE}/two-factor/disable`),

    /** Suspend 2FA - turn the master switch off but KEEP the configured methods
     *  (TOTP key + per-method flags + preferred). Login stops challenging; call
     *  `resumeTwoFactor` to restore the exact same setup without reconfiguring. */
    suspendTwoFactor: () =>
      client.post<void>(`${PROFILE_BASE}/two-factor/suspend`),

    /** Resume a suspended 2FA - turn the master switch back on; the previously
     *  configured methods take effect again immediately. */
    resumeTwoFactor: () =>
      client.post<void>(`${PROFILE_BASE}/two-factor/resume`),

    /** Get TOTP setup info */
    getTotpSetup: () =>
      client.post<TotpSetupDto>(`${PROFILE_BASE}/two-factor/totp/setup`),

    /** Enable TOTP */
    enableTotp: (data: EnableTotpDto) =>
      client.post<void>(`${PROFILE_BASE}/two-factor/totp/enable`, data),

    /** Disable TOTP */
    disableTotp: () =>
      client.post<void>(`${PROFILE_BASE}/two-factor/totp/disable`),

    /** Disable a single 2FA method (SMS / email / TOTP); other methods stay on. */
    disableTwoFactorMethod: (data: TwoFactorMethodRequestDto) =>
      client.post<void>(`${PROFILE_BASE}/two-factor/method/disable`, data),

    /** Set the preferred 2FA method (must be an enabled method). */
    setPreferredTwoFactor: (data: TwoFactorMethodRequestDto) =>
      client.post<void>(`${PROFILE_BASE}/two-factor/preferred`, data),

    // -- Login History --

    /** Get login history */
    getLoginHistory: (params?: LoginHistoryQueryDto) =>
      client.get<LoginLogDto[]>(`${PROFILE_BASE}/login-history`, { params }),

    // -- User Detail --

    /** Get current user detail */
    getDetail: () =>
      client.get<UserDetailDto>(`${PROFILE_BASE}/detail`),

    /** Update current user detail */
    updateDetail: (data: CreateUserDetailDto) =>
      client.put<UserDetailDto>(`${PROFILE_BASE}/detail`, data),

    // -- Account Management --

    /** Deactivate account */
    deactivateAccount: (data?: DeactivateAccountDto) =>
      client.post<void>(`${PROFILE_BASE}/deactivate`, data ?? {}),

    /** Delete account (soft delete, GDPR) */
    deleteAccount: () =>
      client.delete<void>(`${PROFILE_BASE}/account`),

    /** Export personal data (GDPR) */
    exportPersonalData: () =>
      client.get<PersonalDataExportDto>(`${PROFILE_BASE}/personal-data`),

    // -- Change Email/Phone --

    /** Send change email verification code */
    sendChangeEmailCode: (data: SendChangeVerificationCodeDto) =>
      client.post<void>(`${PROFILE_BASE}/change-email/send-code`, data),

    /** Confirm email change */
    confirmChangeEmail: (data: ChangeEmailDto) =>
      client.post<void>(`${PROFILE_BASE}/change-email/confirm`, data),

    /** Send change phone verification code */
    sendChangePhoneCode: (data: SendChangeVerificationCodeDto) =>
      client.post<void>(`${PROFILE_BASE}/change-phone/send-code`, data),

    /** Confirm phone change */
    confirmChangePhone: (data: ChangePhoneNumberDto) =>
      client.post<void>(`${PROFILE_BASE}/change-phone/confirm`, data),
  };
}

// ============================================
// Admin User Management API (DefaultUserAdminController)
// ============================================

export function useAdminUserApi(client: HttpClient) {
  return {
    /** Get user list (paged) */
    getList: (data?: UserListQueryDto) =>
      client.post<PagedList<UserListItemDto>>(`${ADMIN_USER_BASE}/list`, data ?? {}),

    /** Get user by ID */
    getById: (id: string) =>
      client.get<UserDto>(`${ADMIN_USER_BASE}/${id}`),

    /** Create user */
    create: (data: CreateUserDto) =>
      client.post<UserDto>(ADMIN_USER_BASE, data),

    /** Update user */
    update: (id: string, data: UpdateUserDto) =>
      client.put<UserDto>(`${ADMIN_USER_BASE}/${id}`, data),

    /** Delete user */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_USER_BASE}/${id}`),

    /** Enable user */
    enable: (id: string) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/enable`),

    /** Disable user */
    disable: (id: string, reason?: string | null) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/disable`, reason ?? null),

    /** Lock user */
    lock: (id: string, data?: LockUserDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/lock`, data ?? {}),

    /** Unlock user */
    unlock: (id: string) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/unlock`),

    /** Change password (admin) */
    changePassword: (id: string, data: ChangePasswordDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/change-password`, data),

    /** Reset password (admin) */
    resetPassword: (id: string, data: ResetPasswordByAdminDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${id}/reset-password`, data),

    /** Batch create users */
    createMany: (data: CreateUserDto[]) =>
      client.post<UserListItemDto[]>(`${ADMIN_USER_BASE}/batch/create`, data),

    /** Batch update users */
    updateMany: (data: UpdateUserBatchDto[]) =>
      client.put<UserListItemDto[]>(`${ADMIN_USER_BASE}/batch/update`, data),

    /** Batch delete users */
    deleteMany: (ids: string[]) =>
      client.delete<void>(`${ADMIN_USER_BASE}/batch/delete`, { body: ids }),

    /** Assign user to organization */
    assignToOrganization: (userId: string, data: AssignOrganizationDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${userId}/assign-organization`, data),

    /** Remove user from organization */
    removeFromOrganization: (userId: string) =>
      client.post<void>(`${ADMIN_USER_BASE}/${userId}/remove-organization`),

    /** Assign roles to user */
    assignRoles: (userId: string, data: AssignRolesDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${userId}/assign-roles`, data),

    /** Remove roles from user */
    removeRoles: (userId: string, data: RemoveRolesDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${userId}/remove-roles`, data),

    /**
     * Mark a user's email / phone as confirmed.
     *
     * Separate endpoint on purpose: `update` now CLEARS the confirmed flag whenever
     * the address changes (writing it directly used to leave the flag standing, so
     * an account could end up "verified" on an address nobody ever proved). Vouching
     * for an address is a different act - it needs someone to press it deliberately,
     * and it is written to the audit trail.
     *
     * Pass `null` for a field to leave that flag alone.
     */
    confirmContact: (userId: string, data: ConfirmContactDto) =>
      client.post<void>(`${ADMIN_USER_BASE}/${userId}/confirm-contact`, data),

    /** Get user statistics */
    getStatistics: (params?: { organizationId?: string; roleId?: string }) =>
      client.get<UserStatisticsDto>(`${ADMIN_USER_BASE}/statistics`, { params }),

    /** Export users as CSV (Blob download; backend emits UTF-8 BOM) */
    exportCsv: (data?: UserListQueryDto) =>
      client.download(`${ADMIN_USER_BASE}/export/csv`, { method: 'POST', body: data ?? {} }),

    /** Import users from CSV file */
    importCsv: (file: File) =>
      client.upload<UserImportResult>(`${ADMIN_USER_BASE}/import/csv`, file),
  };
}

// ============================================
// Admin Role Management API (DefaultRoleAdminController)
// ============================================

/**
 * Invitation management (admin side): open an account, send the link, resend, revoke.
 *
 * There is deliberately no "list invitations" endpoint - an invitation *is* an account
 * in the `Pending` state, so `useAdminUserApi().getList({ invitationState: 1 })` answers
 * "who has not accepted yet" without a second list to keep in sync.
 */
export function useAdminInvitationApi(client: HttpClient) {
  return {
    /**
     * Open an account and issue an invitation.
     *
     * The returned `acceptUrl` is readable only once - use it to deliver the link
     * yourself, or let the app's `UserInvitedEvent` handler mail it.
     */
    create: (data: CreateInvitationDto) =>
      client.post<InvitationDto>(ADMIN_INVITATION_BASE, data),

    /** Resend the invitation. The previous link stops working immediately. */
    resend: (userId: string, lifetimeHours?: number) =>
      client.post<InvitationDto>(
        `${ADMIN_INVITATION_BASE}/${userId}/resend`
          + (lifetimeHours == null ? '' : `?lifetimeHours=${lifetimeHours}`),
      ),

    /** Revoke: delete the not-yet-accepted account, which invalidates its link. */
    revoke: (userId: string) =>
      client.delete<void>(`${ADMIN_INVITATION_BASE}/${userId}`),
  };
}

/**
 * Invitation acceptance (invitee side). Both endpoints are anonymous - whoever holds
 * the token has, by definition, no account to sign in with yet.
 *
 * `accept` issues the session (`AcceptInvitationResultDto.token`), so it takes
 * the same {@link AuthApiOptions} as `useAuthApi` and must be built with
 * `withCredentials` in a cross-origin cookie-mode deployment.
 */
export function useInvitationApi(client: HttpClient, options: AuthApiOptions = {}) {
  const cookieAware = cookieAwareOptions(options);
  return {
    /** Read what to show on the acceptance page. Does not consume the token. */
    preview: (token: string) =>
      client.get<InvitationPreviewDto>(`${INVITATION_BASE}/${encodeURIComponent(token)}`),

    /**
     * Accept the invitation.
     *
     * `completed: false` means the account is still inactive and the token is still
     * usable - render `remainingSteps` and let the user come back to the same link.
     */
    accept: (data: AcceptInvitationDto) =>
      client.post<AcceptInvitationResultDto>(`${INVITATION_BASE}/accept`, data, cookieAware),
  };
}

export function useAdminRoleApi(client: HttpClient) {
  return {
    /** Get all roles */
    getAll: () =>
      client.get<RoleDto[]>(ADMIN_ROLE_BASE),

    /** Get roles paged list */
    getPagedList: (params?: RoleListQueryDto) =>
      client.get<PagedList<RoleDto>>(`${ADMIN_ROLE_BASE}/paged`, { params }),

    /** Get role detail (with user count) */
    getDetail: (id: string) =>
      client.get<RoleDetailDto>(`${ADMIN_ROLE_BASE}/${id}/detail`),

    /** Get role by ID */
    getById: (id: string) =>
      client.get<RoleDto>(`${ADMIN_ROLE_BASE}/${id}`),

    /** Get role by name. Role names are free text - encode before interpolating. */
    getByName: (name: string) =>
      client.get<RoleDto>(`${ADMIN_ROLE_BASE}/by-name/${encodeURIComponent(name)}`),

    /** Create role */
    create: (data: CreateRoleDto) =>
      client.post<RoleDto>(ADMIN_ROLE_BASE, data),

    /** Update role */
    update: (id: string, data: UpdateRoleDto) =>
      client.put<RoleDto>(`${ADMIN_ROLE_BASE}/${id}`, data),

    /** Delete role */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_ROLE_BASE}/${id}`),

    /** Batch delete roles */
    deleteMany: (ids: string[]) =>
      client.delete<void>(`${ADMIN_ROLE_BASE}/batch`, { body: ids }),

    /** Check if role exists by name */
    exists: (name: string) =>
      client.get<boolean>(`${ADMIN_ROLE_BASE}/exists/${encodeURIComponent(name)}`),

    /** Get users in role (paged) */
    getUsersInRole: (id: string, params?: { pageIndex?: number; pageSize?: number }) =>
      client.get<PagedList<UserListItemDto>>(`${ADMIN_ROLE_BASE}/${id}/users`, { params }),

    /** Get user count in role */
    getUserCount: (id: string) =>
      client.get<number>(`${ADMIN_ROLE_BASE}/${id}/user-count`),

    /** Get default roles */
    getDefaultRoles: () =>
      client.get<RoleDto[]>(`${ADMIN_ROLE_BASE}/defaults`),

    /** Set role as default */
    setDefault: (id: string, isDefault: boolean) =>
      client.put<void>(`${ADMIN_ROLE_BASE}/${id}/default`, null, { params: { isDefault } }),

    // Legacy alias
    /** @deprecated Use getAll() or getPagedList() instead */
    getList: (params?: RoleListQueryDto) =>
      client.get<RoleDto[]>(ADMIN_ROLE_BASE, { params }),
  };
}

// ============================================
// Admin Organization Management API (DefaultOrganizationAdminController)
// ============================================

export function useAdminOrganizationApi(client: HttpClient) {
  return {
    /** Get organization tree */
    getTree: () =>
      client.get<OrganizationTreeNodeDto[]>(`${ADMIN_ORG_BASE}/tree`),

    /** Get organization by ID */
    getById: (id: string) =>
      client.get<OrganizationDto>(`${ADMIN_ORG_BASE}/${id}`),

    /** Create organization */
    create: (data: CreateOrganizationDto) =>
      client.post<OrganizationDto>(ADMIN_ORG_BASE, data),

    /** Update organization */
    update: (id: string, data: UpdateOrganizationDto) =>
      client.put<OrganizationDto>(`${ADMIN_ORG_BASE}/${id}`, data),

    /** Delete organization */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_ORG_BASE}/${id}`),

    /** Move organization to new parent */
    move: (id: string, data: MoveOrganizationDto) =>
      client.post<void>(`${ADMIN_ORG_BASE}/${id}/move`, data),

    /** Get all children of an organization */
    getChildren: (id: string) =>
      client.get<OrganizationDto[]>(`${ADMIN_ORG_BASE}/${id}/children`),

    /** Get all parents of an organization */
    getParents: (id: string) =>
      client.get<OrganizationDto[]>(`${ADMIN_ORG_BASE}/${id}/parents`),

    /** Batch create organizations */
    createMany: (data: CreateOrganizationDto[]) =>
      client.post<OrganizationDto[]>(`${ADMIN_ORG_BASE}/batch/create`, data),

    /** Batch update organizations */
    updateMany: (data: UpdateOrganizationBatchDto[]) =>
      client.put<OrganizationDto[]>(`${ADMIN_ORG_BASE}/batch/update`, data),

    /** Batch delete organizations */
    deleteMany: (ids: string[]) =>
      client.delete<void>(`${ADMIN_ORG_BASE}/batch/delete`, { body: ids }),

    /** Get organization statistics */
    getStatistics: (id: string) =>
      client.get<OrganizationStatisticsDto>(`${ADMIN_ORG_BASE}/${id}/statistics`),

    /** Search organizations by keyword */
    search: (keyword: string, maxResults?: number) =>
      client.get<OrganizationDto[]>(`${ADMIN_ORG_BASE}/search`, { params: { keyword, maxResults } }),

    /** Update sort order */
    updateSortOrder: (id: string, data: UpdateSortOrderDto) =>
      client.put<void>(`${ADMIN_ORG_BASE}/${id}/sort-order`, data),

    /** Batch update sort orders */
    batchUpdateSortOrder: (data: BatchSortOrderItemDto[]) =>
      client.put<void>(`${ADMIN_ORG_BASE}/batch/sort-order`, data),

    /**
     * Reorder organizations inside one parent (drag-and-drop).
     *
     * Prefer this over `batchUpdateSortOrder` for drag results: it takes a
     * relative order and the server merges it by slot, so organizations
     * outside the submitted positions are never pushed around.
     */
    reorder: (data: ReorderRequest, parentId?: string | null) =>
      client.post<void>(`${ADMIN_ORG_BASE}/reorder`, data, {
        params: parentId ? { parentId } : undefined,
      }),

    /** Get users in organization (paged) */
    getUsers: (id: string, params?: { pageIndex?: number; pageSize?: number; includeChildren?: boolean }) =>
      client.get<PagedList<UserListItemDto>>(`${ADMIN_ORG_BASE}/${id}/users`, { params }),

    // Legacy alias
    /** @deprecated Use getTree() instead */
    getList: () =>
      client.get<OrganizationTreeNodeDto[]>(`${ADMIN_ORG_BASE}/tree`),
  };
}

// ============================================
// Admin Session Management API (DefaultSessionAdminController)
// ============================================

export function useAdminSessionApi(client: HttpClient) {
  return {
    /**
     * Paged session list - `userId` optional; omitted returns the global
     * list across all users (sorted by last activity desc) with `userName`
     * populated on every item.
     */
    getSessions: (params?: { userId?: string; includeRevoked?: boolean; pageIndex?: number; pageSize?: number }) =>
      client.get<PagedList<UserSessionDto>>(ADMIN_SESSION_BASE, { params }),

    /** Get user sessions */
    getUserSessions: (userId: string, params?: { includeRevoked?: boolean }) =>
      client.get<UserSessionDto[]>(`${ADMIN_SESSION_BASE}/user/${userId}`, { params }),

    /** Revoke a session */
    revokeSession: (sessionId: string) =>
      client.post<void>(`${ADMIN_SESSION_BASE}/${sessionId}/revoke`),

    /** Revoke all user sessions */
    revokeAllSessions: (userId: string, excludeSessionId?: string | null) =>
      client.post<void>(`${ADMIN_SESSION_BASE}/user/${userId}/revoke-all`, excludeSessionId ?? null),

    /** Update session activity time */
    updateActivityTime: (sessionId: string) =>
      client.post<void>(`${ADMIN_SESSION_BASE}/${sessionId}/update-activity`),

    /** Clean expired sessions */
    cleanExpired: (inactiveMinutes?: number) =>
      client.post<number>(`${ADMIN_SESSION_BASE}/clean-expired`, null, { params: { inactiveMinutes } }),

    /** Get session statistics */
    getStatistics: () =>
      client.get<SessionStatisticsDto>(`${ADMIN_SESSION_BASE}/statistics`),

    /** List currently active users (sorted by last activity desc). */
    getActiveUsers: (top?: number) =>
      client.get<ActiveUserSummaryDto[]>(`${ADMIN_SESSION_BASE}/active-users`, { params: { top } }),
  };
}

// ============================================
// Admin Login Log API (DefaultLoginLogAdminController)
// ============================================

export function useAdminLoginLogApi(client: HttpClient) {
  return {
    /** Get login log by ID */
    getById: (id: string) =>
      client.get<LoginLogDto>(`${ADMIN_LOGIN_LOG_BASE}/${id}`),

    /** Get login logs (paged) */
    getList: (data: LoginLogQueryDto) =>
      client.post<PagedList<LoginLogDto>>(`${ADMIN_LOGIN_LOG_BASE}/list`, data),

    /** Get user login logs */
    getUserLoginLogs: (userId: string, params?: { startDate?: string; endDate?: string; isSuccess?: boolean }) =>
      client.get<LoginLogDto[]>(`${ADMIN_LOGIN_LOG_BASE}/user/${userId}`, { params }),

    /** Get login statistics */
    getStatistics: (params?: { startDate?: string; endDate?: string }) =>
      client.get<LoginStatisticsDto>(`${ADMIN_LOGIN_LOG_BASE}/statistics`, { params }),

    /** Get user login statistics */
    getUserStatistics: (userId: string, params?: { startDate?: string; endDate?: string }) =>
      client.get<UserLoginStatisticsDto>(`${ADMIN_LOGIN_LOG_BASE}/statistics/user/${userId}`, { params }),

    /** Get failed login attempts */
    getFailedAttempts: (params?: { startDate?: string; endDate?: string; top?: number }) =>
      client.get<LoginLogDto[]>(`${ADMIN_LOGIN_LOG_BASE}/failed-attempts`, { params }),

    /** Delete expired login logs */
    deleteExpired: (days?: number) =>
      client.delete<number>(`${ADMIN_LOGIN_LOG_BASE}/expired`, { params: { days } }),

    /** Get login trend data */
    getLoginTrend: (startDate: string, endDate: string, userId?: string) =>
      client.get<LoginTrendItem[]>(`${ADMIN_LOGIN_LOG_BASE}/trend`, { params: { startDate, endDate, userId } }),
  };
}

// ============================================
// Admin Login Security API (DefaultLoginSecurityAdminController)
// ============================================

export function useAdminLoginSecurityApi(client: HttpClient) {
  return {
    /** Get security overview (dashboard) */
    getOverview: (hours?: number) =>
      client.get<SecurityOverviewDto>(`${ADMIN_LOGIN_SECURITY_BASE}/overview`, { params: { hours } }),

    /** Get users with frequent login failures */
    getFrequentFailures: (params?: { hours?: number; minFailures?: number }) =>
      client.get<UserFailedLoginSummaryDto[]>(`${ADMIN_LOGIN_SECURITY_BASE}/frequent-failures`, { params }),

    /** Get user recent logins */
    getUserRecentLogins: (userId: string, count?: number) =>
      client.get<LoginLogDto[]>(`${ADMIN_LOGIN_SECURITY_BASE}/user/${userId}/recent-logins`, { params: { count } }),

    /** Get user frequent IP addresses */
    getUserFrequentIps: (userId: string) =>
      client.get<string[]>(`${ADMIN_LOGIN_SECURITY_BASE}/user/${userId}/frequent-ips`),

    /** Detect abnormal login risk for a user */
    detectAbnormalLogin: (userId: string, params?: { ipAddress?: string; userAgent?: string }) =>
      client.post<AbnormalLoginResultDto>(`${ADMIN_LOGIN_SECURITY_BASE}/user/${userId}/detect-abnormal`, null, { params }),
  };
}

// ============================================
// Admin User Detail API (DefaultUserDetailAdminController)
// ============================================

export function useAdminUserDetailApi(client: HttpClient) {
  return {
    /** Get user detail by user ID */
    getByUserId: (userId: string) =>
      client.get<UserDetailDto>(`${ADMIN_USER_DETAIL_BASE}/user/${userId}`),

    /** Create or update user detail */
    createOrUpdate: (userId: string, data: CreateUserDetailDto) =>
      client.post<UserDetailDto>(`${ADMIN_USER_DETAIL_BASE}/user/${userId}`, data),

    /** Delete user detail */
    delete: (userId: string) =>
      client.delete<void>(`${ADMIN_USER_DETAIL_BASE}/user/${userId}`),
  };
}

// ============================================
// Admin Tenant API (DefaultTenantAdminController)
// ============================================

export function useAdminTenantApi(client: HttpClient) {
  return {
    /** Get tenants (paged) */
    getPagedList: (params?: TenantQueryDto) =>
      client.get<PagedList<TenantDto>>(`${ADMIN_TENANT_BASE}/paged`, { params }),

    /** Get tenant by ID */
    getById: (id: string) =>
      client.get<TenantDto>(`${ADMIN_TENANT_BASE}/${id}`),

    /** Get tenant by code. Tenant codes are free text - encode before interpolating. */
    getByCode: (code: string) =>
      client.get<TenantDto>(`${ADMIN_TENANT_BASE}/by-code/${encodeURIComponent(code)}`),

    /** Create tenant */
    create: (data: CreateTenantDto) =>
      client.post<TenantDto>(ADMIN_TENANT_BASE, data),

    /** Update tenant */
    update: (id: string, data: UpdateTenantDto) =>
      client.put<TenantDto>(`${ADMIN_TENANT_BASE}/${id}`, data),

    /** Enable/disable tenant */
    setEnabled: (id: string, enabled: boolean) =>
      client.put<void>(`${ADMIN_TENANT_BASE}/${id}/enabled`, null, { params: { enabled } }),

    /** Delete tenant */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_TENANT_BASE}/${id}`),
  };
}
