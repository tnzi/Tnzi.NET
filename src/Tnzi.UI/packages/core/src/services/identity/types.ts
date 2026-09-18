/**
 * Identity Module Types - User, Role, Session, Organization, Auth management
 * Aligned with Tnzi.NET backend Identity module
 */

import type { PagedQueryDto, SortedPagedQueryDto } from '../../types/pagination';
import type { Flags } from '../../utils/flags';
import { Gender, OAuthProvider, TwoFactorType, PasswordStrengthLevel, AbnormalLoginType, AbnormalLoginAction, LoginStatus } from './metadata';
import type { CaptchaChallengeDto, CaptchaClientConfigDto } from '../captcha/types';

export { Gender, OAuthProvider, TwoFactorType, PasswordStrengthLevel, AbnormalLoginType, AbnormalLoginAction, LoginStatus };

// ============================================
// User Types
// ============================================

/**
 * User list item DTO (simplified for lists, no UserDetail fields)
 */
export interface UserListItemDto {
  id: string;
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  isLockedOut: boolean;
  /**
   * What this account still owes: an unaccepted invitation, a forced password change, ...
   *
   * Kept separate from `isLockedOut` on purpose: an invited account is locked out too,
   * but "a new hire has not arrived yet" and "this person was disabled" are different
   * facts, and an admin list that merges them into one status is unusable.
   */
  pendingActions: Flags<PendingUserActions>;
  isEmailConfirmed: boolean;
  isPhoneNumberConfirmed: boolean;
  twoFactorEnabled: boolean;
  lockoutEnd?: Date | string | null;
  accessFailedCount: number;
  creationTime: Date | string;
  lastModificationTime?: Date | string | null;
  roles: string[];
}

/**
 * User DTO - full user information (includes UserDetail fields)
 */
export interface UserDto extends UserListItemDto {
  firstName?: string | null;
  lastName?: string | null;
  nickname?: string | null;
  avatar?: string | null;
  avatarId?: string | null;
  gender: number;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
}

/**
 * Current user profile (for auth context)
 */
export interface UserProfile {
  id: string;
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  nickname?: string | null;
  avatar?: string | null;
  roles: string[];
  permissions: string[];
}

/**
 * Create user request
 */
export interface CreateUserDto {
  userName: string;
  password: string;
  email?: string | null;
  phoneNumber?: string | null;
  nickname?: string | null;
  organizationId?: string | null;
  roleIds?: string[];
}

/**
 * Update user request
 */
export interface UpdateUserDto {
  email?: string | null;
  phoneNumber?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  nickname?: string | null;
  avatarUrl?: string | null;
  avatarId?: string | null;
  gender?: number;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
  organizationId?: string | null;
  roleIds?: string[];
}

/**
 * Fields a user may change on their own profile.
 *
 * Deliberately has no `roleIds` / `organizationId` — that is the entire reason
 * this type exists separately from `UpdateUserDto` (the admin-surface shape).
 * `PUT /users/profile` used to accept the admin DTO, so any signed-in user
 * could grant themselves a role or move themselves into another org.
 * Keep privileged fields out of this type; add self-service fields here.
 */
export interface UpdateProfileDto {
  // NOTE: `email` / `phoneNumber` are deliberately absent. They used to be here,
  // which let this endpoint change contact details with no verification at all -
  // right next to the change-email / change-phone flow that sends a code to the
  // NEW address. Worse, writing them directly left `emailConfirmed` /
  // `phoneNumberConfirmed` untouched, so an account ended up "verified" on an
  // address it had never proven. Use `changeEmail` / `changePhone` instead.
  firstName?: string | null;
  lastName?: string | null;
  nickname?: string | null;
  avatarUrl?: string | null;
  avatarId?: string | null;
  gender?: number;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
}

/**
 * Admin request to mark a user's contact details as confirmed.
 * `null` on a field leaves that flag untouched.
 */
export interface ConfirmContactDto {
  confirmEmail?: boolean | null;
  confirmPhoneNumber?: boolean | null;
}

/**
 * Change password request
 */
export interface ChangePasswordDto {
  currentPassword: string;
  newPassword: string;
}

/**
 * Admin reset password request
 */
export interface ResetPasswordByAdminDto {
  newPassword: string;
}

/**
 * Lock user request
 */
export interface LockUserDto {
  lockoutEnd?: Date | string | null;
  reason?: string | null;
}

/**
 * Assign organization request
 */
export interface AssignOrganizationDto {
  organizationId: string;
}

/**
 * Assign roles request
 */
export interface AssignRolesDto {
  roleIds: string[];
}

/**
 * Remove roles request
 */
export interface RemoveRolesDto {
  roleIds: string[];
}

/**
 * User list query parameters
 */
export interface UserListQueryDto extends SortedPagedQueryDto {
  keyword?: string;
  organizationId?: string;
  roleId?: string;
  isLockedOut?: boolean;
  isEmailConfirmed?: boolean;
  /** Filter by outstanding action: matches if **any** of the given bits is owed. */
  pendingAction?: Flags<PendingUserActions>;
}

/**
 * User statistics DTO
 */
export interface UserStatisticsDto {
  totalUsers: number;
  activeUsers: number;
  lockedUsers: number;
  usersByOrganization: number;
  usersByRole: number;
  recentRegistrations: number;
}

/**
 * User import result
 */
export interface UserImportResult {
  totalRows: number;
  successCount: number;
  failedCount: number;
  skippedCount: number;
  errors: Record<number, string>;
}

/**
 * Batch update user DTO
 */
export interface UpdateUserBatchDto {
  id: string;
  dto: UpdateUserDto;
}

/**
 * Deactivate account request
 */
export interface DeactivateAccountDto {
  reason?: string | null;
}

/**
 * Personal data export DTO (GDPR)
 */
export interface PersonalDataExportDto {
  userId: string;
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  organizationName?: string | null;
  roles: string[];
  creationTime: Date | string;
  lastModificationTime?: Date | string | null;
  twoFactorEnabled: boolean;
  emailConfirmed: boolean;
  phoneNumberConfirmed: boolean;
  linkedProviders: string[];
  exportedAt: Date | string;
}

/**
 * Change email request
 */
export interface ChangeEmailDto {
  newEmail: string;
  code: string;
}

/**
 * Change phone number request
 */
export interface ChangePhoneNumberDto {
  newPhoneNumber: string;
  code: string;
}

/**
 * Send change verification code request
 */
export interface SendChangeVerificationCodeDto {
  newAddress: string;
}

/**
 * Login history query parameters
 */
export interface LoginHistoryQueryDto {
  startDate?: Date | string;
  endDate?: Date | string;
  isSuccess?: boolean;
}

// ============================================
// User Detail Types
// ============================================

/**
 * User detail DTO
 */
export interface UserDetailDto {
  userId: string;
  firstName?: string | null;
  lastName?: string | null;
  fullName?: string | null;
  nickname?: string | null;
  avatarUrl?: string | null;
  avatarId?: string | null;
  gender: number;
  birthday?: Date | string | null;
  address?: string | null;
  bio?: string | null;
  website?: string | null;
  creationTime: Date | string;
  lastModificationTime?: Date | string | null;
}

/**
 * Create/update user detail request
 */
export interface CreateUserDetailDto {
  firstName?: string | null;
  lastName?: string | null;
  nickname?: string | null;
  avatarUrl?: string | null;
  avatarId?: string | null;
  gender?: number;
  birthday?: Date | string | null;
  address?: string | null;
  bio?: string | null;
  website?: string | null;
}

// ============================================
// Role Types
// ============================================

/**
 * Role DTO
 */
export interface RoleDto {
  id: string;
  name: string;
  normalizedName?: string | null;
  description?: string | null;
  isSystem: boolean;
  isDefault: boolean;
  creationTime: Date | string;
}

/**
 * Role detail DTO (with user count)
 */
export interface RoleDetailDto extends RoleDto {
  userCount: number;
}

/**
 * Create role request
 */
export interface CreateRoleDto {
  name: string;
  description?: string | null;
  isDefault?: boolean;
}

/**
 * Update role request
 */
export interface UpdateRoleDto {
  name: string;
  description?: string | null;
  isDefault?: boolean;
}

/**
 * Role list query parameters
 */
export interface RoleListQueryDto extends PagedQueryDto {
  keyword?: string;
  isSystem?: boolean;
  isDefault?: boolean;
}

// ============================================
// Session Types
// ============================================

/**
 * User session DTO
 */
export interface UserSessionDto {
  id: string;
  userId: string;
  /**
   * User name - populated by the global session list (GET /admin/sessions,
   * batch-joined against the user table); the per-user endpoints leave it
   * null (the caller already knows the user).
   */
  userName?: string | null;
  deviceInfo?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  creationTime: Date | string;
  lastActivityTime: Date | string;
  /** Sliding hard expiry; pushed forward on every token refresh. */
  expiresAt?: Date | string | null;
  /**
   * Absolute ceiling on the session's lifetime, fixed when it was created -
   * renewals never push it. Null means the deployment set no absolute cap.
   */
  absoluteExpiresAt?: Date | string | null;
  isRevoked: boolean;
  revokedAt?: Date | string | null;
}

/**
 * Session statistics DTO
 */
export interface SessionStatisticsDto {
  activeSessionCount: number;
  onlineUserCount: number;
  topDevices: DeviceStatItem[];
}

/**
 * Device statistics item
 */
export interface DeviceStatItem {
  deviceInfo: string;
  count: number;
}

/**
 * Active user summary - backs the admin "active users" picker on the session
 * management page. Lets admins discover who is currently signed in without
 * exposing a full user list.
 */
export interface ActiveUserSummaryDto {
  userId: string;
  userName?: string | null;
  sessionCount: number;
  lastActivityTime: Date | string;
}

// ============================================
// Organization Types
// ============================================

/**
 * Organization DTO
 */
export interface OrganizationDto {
  id: string;
  name: string;
  code?: string | null;
  remark?: string | null;
  parentId?: string | null;
  sortOrder: number;
  isEnabled: boolean;
  path?: string | null;
  level: number;
  creationTime: Date | string;
}

/**
 * Organization tree node DTO
 */
export interface OrganizationTreeNodeDto extends OrganizationDto {
  children: OrganizationTreeNodeDto[];
}

/**
 * Create organization request
 */
export interface CreateOrganizationDto {
  name: string;
  code?: string | null;
  remark?: string | null;
  parentId?: string | null;
  sortOrder?: number;
}

/**
 * Update organization request
 */
export interface UpdateOrganizationDto {
  name: string;
  code?: string | null;
  remark?: string | null;
  sortOrder?: number;
  isEnabled?: boolean;
}

/**
 * Move organization request
 */
export interface MoveOrganizationDto {
  newParentId?: string | null;
}

/**
 * Update sort order request
 */
export interface UpdateSortOrderDto {
  sortOrder: number;
}

/**
 * Batch sort order item
 */
export interface BatchSortOrderItemDto {
  id: string;
  sortOrder: number;
}

/**
 * Organization statistics DTO
 */
export interface OrganizationStatisticsDto {
  directChildren: number;
  totalChildren: number;
  directUsers: number;
  totalUsers: number;
}

/**
 * Batch update organization DTO
 */
export interface UpdateOrganizationBatchDto {
  id: string;
  dto: UpdateOrganizationDto;
}

// ============================================
// Auth Types
// ============================================

/**
 * Public auth configuration returned by `GET /auth/config` (anonymous).
 * Drives which login methods / register / recovery / third-party buttons the
 * login page shows, per backend deployment config. Contains only boolean
 * switches and the list of enabled OAuth providers - never any secret.
 */
export interface AuthConfigDto {
  // Account identifiers accepted by password login
  allowUserNameLogin: boolean;
  allowEmailLogin: boolean;
  allowSmsLogin: boolean;
  useEmailAsUserName: boolean;
  // Code login
  enableCodeLogin: boolean;
  codeLoginViaSms: boolean;
  codeLoginViaEmail: boolean;
  // Registration
  /** Any registration path is open (self-registration OR either quick-register channel). */
  enableRegistration: boolean;
  /**
   * Classic self-registration (username + password) is open
   * (`Identity:Registration:EnableSelfRegistration`, default false).
   *
   * NOTE: this flag is new. Before it existed, `POST auth/register` had no switch
   * at all, so `enableRegistration` could report false while the endpoint happily
   * created accounts. Gate the password sign-up form on THIS flag, not on
   * `enableRegistration` (which is the union across all three paths).
   */
  registerViaPassword: boolean;
  registerViaSms: boolean;
  registerViaEmail: boolean;
  // Password recovery
  enablePasswordRecovery: boolean;
  recoveryViaEmail: boolean;
  recoveryViaSms: boolean;
  // Captcha (human verification)
  /** Password login is gated adaptively (after repeated failures); code-login send-code unconditionally. */
  enableCaptchaOnLogin: boolean;
  /** Register / quick-register send-code / resend-confirmation are gated unconditionally. */
  enableCaptchaOnRegister: boolean;
  /** `forgot-password` and `password-recovery/send-code` are gated unconditionally. */
  enableCaptchaOnPasswordRecovery: boolean;
  /**
   * Which captcha provider to render and with what (same payload as `GET /captcha/config`).
   * With Tnzi.Identity loaded `enabled` is always true: an unconfigured deployment falls back
   * to the built-in `image` provider.
   */
  captcha: CaptchaClientConfigDto;
  /**
   * Passkeys are enabled for this deployment (`Identity:Passkey:Enabled`).
   *
   * Says the deployment allows them - NOT that this browser can run the
   * ceremony. Gate the UI on this AND `isPasskeySupported()`.
   */
  enablePasskey: boolean;
  // Third-party (only enabled providers are listed).
  // NOTE: camelCase of C# `OAuthProviders` is `oAuthProviders` (only the first
  // letter is lowered), not `oauthProviders`.
  oAuthProviders: OAuthProviderInfoDto[];
}

/**
 * An enabled third-party login provider (public, no secrets).
 */
export interface OAuthProviderInfoDto {
  /** Provider key (lowercase, used to build `/auth/oauth/{provider}/login`). */
  provider: string;
  /** Display name (e.g. "GitHub"). */
  displayName: string;
}

/**
 * Login request
 */
export interface LoginDto {
  userName: string;
  password: string;
  captchaId?: string;
  captchaCode?: string;
  /**
   * Captcha token from the unified widget (any provider). Required once the adaptive login captcha has been demanded.
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
}

/**
 * Token result (from login/refresh)
 */
export interface TokenResultDto {
  accessToken: string;
  refreshToken: string;
  expiresAt: Date | string;
  expiresIn: number;
}

/**
 * Register request
 */
export interface RegisterDto {
  userName?: string;
  email: string;
  password: string;
  captchaId?: string;
  captchaCode?: string;
  /**
   * Captcha token from the unified widget (any provider). Required when the register captcha is on.
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
  firstName?: string;
  lastName?: string;
}

/**
 * Refresh token request
 */
export interface RefreshTokenDto {
  /**
   * The refresh token.
   *
   * Optional because the backend can be configured to deliver the refresh token
   * as an `HttpOnly` cookie (`Identity:TokenDelivery:Mode = Cookie`); in that mode
   * the browser carries it and the body carries nothing - the client literally
   * cannot read the value it is refreshing with, which is the point.
   */
  refreshToken?: string;
}

/**
 * Forgot password request
 */
export interface ForgotPasswordDto {
  email: string;
  /**
   * Captcha token from the unified widget (any provider). Required when the password-recovery captcha is on.
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
  captchaId?: string | null;
  captchaCode?: string | null;
}

/**
 * Reset password request
 */
export interface ResetPasswordDto {
  email: string;
  token: string;
  newPassword: string;
}

/**
 * Captcha challenge DTO - what `GET /auth/captcha/{purpose}/json` returns and what rides in the
 * `errorDetails` of `IDENTITY_CAPTCHA_REQUIRED`. Only the `image` provider fills the picture fields.
 */
export type CaptchaDto = CaptchaChallengeDto;

/**
 * Resend email confirmation request
 */
export interface ResendEmailConfirmationDto {
  userId?: string | null;
  email?: string | null;
  /**
   * Captcha token from the unified widget (any provider). Required when the register captcha is on (this endpoint sends a real email per call).
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
  captchaId?: string | null;
  captchaCode?: string | null;
}

/**
 * Password strength result
 */
export interface PasswordStrengthResultDto {
  score: number;
  level: PasswordStrengthLevel;
  suggestions: string[];
  meetsPolicy: boolean;
}

// ============================================
// Quick Register Types
// ============================================

/**
 * Send quick register code request
 */
export interface SendQuickRegisterCodeDto {
  email?: string | null;
  phoneNumber?: string | null;
  /** Image-captcha id (required when the backend enables the register captcha). */
  captchaId?: string;
  /** Image-captcha code the user typed (required when the register captcha is on). */
  captchaCode?: string;
  /**
   * Captcha token from the unified widget (any provider). Required when the register captcha is on.
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
}

/**
 * Quick register request
 */
export interface QuickRegisterDto {
  email?: string | null;
  phoneNumber?: string | null;
  code: string;
  userName?: string | null;
  firstName?: string | null;
  lastName?: string | null;
}

/**
 * Quick register result
 */
export interface QuickRegisterResultDto {
  userId: string;
  userName: string;
  requirePasswordSetup: boolean;
  setPasswordToken?: string | null;
}

/**
 * Set password request (after quick register)
 */
export interface SetPasswordDto {
  userId: string;
  token: string;
  password: string;
}

// ============================================
// Code Login Types
// ============================================

/**
 * Send code login code request
 */
export interface SendCodeLoginCodeDto {
  email?: string | null;
  phoneNumber?: string | null;
  type: TwoFactorType;
  /**
   * Image-captcha id. Required when the deployment has `EnableCaptchaOnLogin`
   * on: this endpoint spends a real SMS / email on every call, so it is gated
   * unconditionally rather than adaptively like the password form.
   */
  captchaId?: string | null;
  /** Image-captcha text the user typed. */
  captchaCode?: string | null;
  /**
   * Captcha token from the unified widget (any provider). Required when the login captcha is on.
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
}

/**
 * Code login request
 */
export interface CodeLoginDto {
  email?: string | null;
  phoneNumber?: string | null;
  code: string;
  type: TwoFactorType;
}

/**
 * Code login result
 */
export interface CodeLoginResultDto {
  accessToken?: string | null;
  refreshToken?: string | null;
  expiresIn: number;
  refreshTokenExpiresIn?: number | null;
  requirePasswordSetup: boolean;
  setPasswordToken?: string | null;
  userId: string;
  userName?: string | null;
  isNewUser: boolean;
}

// ============================================
// Password Recovery Types
// ============================================

/**
 * Send password recovery code request
 */
export interface SendPasswordRecoveryCodeDto {
  email?: string | null;
  phoneNumber?: string | null;
  type: TwoFactorType;
  /**
   * Captcha token from the unified widget (any provider). Required when the password-recovery captcha is on (this endpoint spends a real SMS / email per call).
   * Either this or the legacy `captchaId` + `captchaCode` pair; this wins when both are set.
   */
  captchaToken?: string | null;
  captchaId?: string | null;
  captchaCode?: string | null;
}

/**
 * Reset password by code request
 */
export interface ResetPasswordByCodeDto {
  email?: string | null;
  phoneNumber?: string | null;
  code: string;
  newPassword: string;
  type: TwoFactorType;
}

// ============================================
// Two-Factor Auth Types
// ============================================

/**
 * Enable 2FA request
 */
export interface EnableTwoFactorDto {
  type: TwoFactorType;
}

/**
 * Send 2FA code request
 */
export interface SendTwoFactorCodeDto {
  tempToken: string;
  type: TwoFactorType;
}

/**
 * 2FA challenge response
 */
export interface TwoFactorChallengeDto {
  tempToken: string;
  supportedTypes: TwoFactorType[];
  codeSent: boolean;
  maskedAddress?: string | null;
  requiresTwoFactor: boolean;
}

/**
 * Verify 2FA and login request
 */
export interface VerifyTwoFactorDto {
  tempToken: string;
  code: string;
  type: TwoFactorType;
}

/**
 * Per-method 2FA state (one entry per method type).
 */
export interface TwoFactorMethodDto {
  /** Method type. */
  type: TwoFactorType;
  /** Can be configured/enabled (address confirmed for SMS/email; TOTP always). */
  available: boolean;
  /** Currently enabled. */
  enabled: boolean;
  /** Is the user's preferred method (shown first at login). */
  isPreferred: boolean;
  /** The channel is enabled at the deployment level, but the user hasn't set up /
   *  verified the matching address (phone / email) yet - so it can't be enabled
   *  until they do. The UI shows the row disabled with a "verify your …" hint.
   *  Always false for TOTP (no address needed). */
  requiresAddress?: boolean;
}

/**
 * 2FA status DTO. Each method (SMS / email / TOTP) is independently enableable;
 * `isEnabled` is the aggregate (any method on). `methods` carries per-method
 * state; `preferredType` is the login-default method.
 */
export interface TwoFactorStatusDto {
  isEnabled: boolean;
  supportedTypes: TwoFactorType[];
  /** @deprecated alias of `preferredType`. */
  currentType?: TwoFactorType | null;
  isTotpEnabled: boolean;
  preferredType?: TwoFactorType | null;
  methods: TwoFactorMethodDto[];
}

/**
 * Request carrying a single 2FA method type (disable-method / set-preferred).
 */
export interface TwoFactorMethodRequestDto {
  type: TwoFactorType;
}

/**
 * TOTP setup DTO
 */
export interface TotpSetupDto {
  sharedKey: string;
  authenticatorUri: string;
}

/**
 * Enable TOTP request
 */
export interface EnableTotpDto {
  verificationCode: string;
}

// ============================================
// OAuth Types
// ============================================

/**
 * OAuth callback result
 */
export interface OAuthCallbackResultDto {
  success: boolean;
  accessToken?: string | null;
  refreshToken?: string | null;
  expiresAt?: Date | string | null;
  requiresRegistration: boolean;
  userInfo?: OAuthUserInfoDto | null;
  errorMessage?: string | null;
  /**
   * Business error code, for branching on a failed callback.
   *
   * Third-party sign-in now goes through the same token exit as every other
   * sign-in method, and that exit carries challenges as FAILURE envelopes:
   * `2FA_REQUIRED` and `IDENTITY_PENDING_ACTIONS_REQUIRED` both arrive here with
   * the temp token in {@link errorDetails}. A callback handler that only looks at
   * `success` will show "login failed" to a user who simply needs to enter their
   * second factor.
   *
   * Also carries `IDENTITY_OAUTH_LINK_CONFIRMATION_REQUIRED`: the provider's email
   * was not asserted as verified, so it was NOT auto-linked to the existing local
   * account. Tell the user to sign in normally and link from their profile.
   */
  errorCode?: string | null;
  /** Envelope details for the code above (challenge temp token, supported methods, …). */
  errorDetails?: unknown;
}

/**
 * OAuth user info DTO
 */
export interface OAuthUserInfoDto {
  provider: string;
  providerKey: string;
  email?: string | null;
  userName?: string | null;
  displayName?: string | null;
  avatarUrl?: string | null;
}

/**
 * User login (linked account) DTO
 */
export interface UserLoginDto {
  userId: string;
  loginProvider: string;
  providerKey: string;
  providerDisplayName?: string | null;
}

/**
 * One-time token that turns the next OAuth start into an account *link* for
 * the signed-in user (`POST /users/profile/linked-accounts/{provider}/link-token`).
 *
 * The OAuth start and callback endpoints are anonymous full-page navigations
 * with no bearer on them, so this token is the only way the callback learns
 * who is linking. Pass `token` as `linkToken` to `oauthLoginUrl`.
 */
export interface OAuthLinkTokenDto {
  token: string;
  /** Lower-cased provider the token is bound to. */
  provider: string;
  expiresAt: string;
}

// ============================================
// Login Log Types
// ============================================

/**
 * Login log DTO
 */
export interface LoginLogDto {
  id: string;
  userId?: string | null;
  userName?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  isSuccess: boolean;
  failureReason?: string | null;
  loginTime: Date | string;
}

/**
 * Login log query parameters
 */
export interface LoginLogQueryDto extends PagedQueryDto {
  userId?: string;
  ipAddress?: string;
  startDate?: Date | string;
  endDate?: Date | string;
  status?: LoginStatus;
  isSuccess?: boolean;
}

/**
 * Login statistics DTO
 */
export interface LoginStatisticsDto {
  totalLogins: number;
  successfulLogins: number;
  failedLogins: number;
  uniqueIpCount: number;
  lastLoginTime?: Date | string | null;
  lastLoginIp?: string | null;
}

/**
 * User login statistics DTO
 */
export interface UserLoginStatisticsDto {
  userId: string;
  totalLogins: number;
  successfulLogins: number;
  failedLogins: number;
  lastLoginTime?: Date | string | null;
  lastLoginIp?: string | null;
}

/**
 * Login trend item
 */
export interface LoginTrendItem {
  date: Date | string;
  totalLogins: number;
  successfulLogins: number;
  failedLogins: number;
  uniqueUsers: number;
}

// ============================================
// Login Security Types
// ============================================

/**
 * Security overview DTO
 */
export interface SecurityOverviewDto {
  timeRangeHours: number;
  totalLoginAttempts: number;
  successfulLogins: number;
  failedLogins: number;
  failureRate: number;
  distinctUsers: number;
  distinctIpAddresses: number;
  lockedOutUsers: number;
}

/**
 * User failed login summary DTO
 */
export interface UserFailedLoginSummaryDto {
  userId: string;
  userName?: string | null;
  email?: string | null;
  failureCount: number;
  lastFailureTime?: Date | string | null;
  ipAddresses: string[];
  isLockedOut: boolean;
}

/**
 * Abnormal login detection result
 */
export interface AbnormalLoginResultDto {
  isAbnormal: boolean;
  abnormalTypes: AbnormalLoginType[];
  riskLevel: number;
  details?: string | null;
  recommendedAction: AbnormalLoginAction;
}

// ============================================
// Tenant Types
// ============================================

/**
 * Tenant DTO
 */
export interface TenantDto {
  id: string;
  name: string;
  code?: string | null;
  isEnabled: boolean;
  expiredAt?: Date | string | null;
  remark?: string | null;
  creationTime: Date | string;
}

/**
 * Tenant query parameters
 */
export interface TenantQueryDto extends PagedQueryDto {
  keyword?: string;
  isEnabled?: boolean;
}

/**
 * Create tenant request
 */
export interface CreateTenantDto {
  name: string;
  code?: string | null;
  isEnabled?: boolean;
  expiredAt?: Date | string | null;
  remark?: string | null;
}

/**
 * Update tenant request
 */
export interface UpdateTenantDto {
  name: string;
  code?: string | null;
  isEnabled?: boolean;
  expiredAt?: Date | string | null;
  remark?: string | null;
}

// ============================================
// Frontend-specific Auth Types
// ============================================

/**
 * Login result DTO (frontend composite type).
 * Combines token data with user profile for convenience.
 * The backend returns TokenResultDto without user; the frontend
 * fetches user profile separately and combines them here.
 */
export interface LoginResultDto {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserProfile;
}

// ============================================
// Passkey (WebAuthn)
// ============================================

/**
 * Output of a passkey `begin` step: browser options plus an opaque handle for
 * the server-side challenge.
 *
 * The challenge is NOT kept in the client. ASP.NET Core's SignInManager passes
 * it through a cookie, which would bind the flow to same-origin browsers; the
 * framework stores it server-side instead and hands back only this handle, so
 * cross-origin frontends and native clients work the same way.
 */
export interface PasskeyOptionsDto {
  /** Feed straight into `navigator.credentials.create()` / `.get()`. */
  optionsJson: string;
  /** Pass back to the matching `complete` call. Single-use. */
  stateId: string;
}

/** Input of a passkey `complete` step. */
export interface PasskeyCompleteDto {
  stateId: string;
  /** JSON-serialised result of `navigator.credentials.create()` / `.get()`. */
  credentialJson: string;
  /** Optional label for the credential ("My iPhone"); registration only. */
  deviceName?: string;
}

/** A registered passkey credential. */
export interface PasskeyCredentialDto {
  /** base64url credential id. */
  credentialId: string;
  name?: string | null;
  createdAt: string;
  /** Synced to a cloud keychain, so it survives replacing the device. */
  isBackedUp: boolean;
}

/** Input of `beginPasskeyAssertion`. */
export interface PasskeyAssertionBeginDto {
  /** Omit for a discoverable-credential flow. */
  userName?: string;
}

// ============================================
// Step-up (re-authentication for one action)
// ============================================

/**
 * The outcome of one step-up verification.
 *
 * There is no token here on purpose: the grant lives server-side. Handing a
 * client-held credential back would create one more thing that can be stolen,
 * and it does not need to exist.
 */
export interface StepUpGrantDto {
  /** The scope this grant covers. */
  scope: string;
  /** UTC expiry. */
  expiresAt: string;
  /** Whether the grant is consumed by the first protected call that uses it. */
  singleUse: boolean;
}

/** Input of `stepUpSendCode`. */
export interface SendStepUpCodeDto {
  /** Channel to deliver on. TOTP is generated by the authenticator, not sent. */
  type: TwoFactorType;
}

/** Input of `stepUpWithCode`. */
export interface StepUpCodeDto {
  code: string;
  type: TwoFactorType;
  /** Must match the scope declared on the endpoint you are about to call. */
  scope: string;
}

/** Input of `stepUpWithPasskey`. */
export interface StepUpPasskeyDto extends PasskeyCompleteDto {
  /** Must match the scope declared on the endpoint you are about to call. */
  scope: string;
}


/**
 * What an account still owes before it can be used. Mirrors the backend `PendingUserActions`.
 *
 * The bit position carries meaning. Low bits **block sign-in outright** (the login guard
 * rejects them). High bits are **obligations**: credentials already checked out, but the
 * person must complete something before getting a token, the same shape as a 2FA challenge.
 *
 * ★★ **Do not test it with `&`.** The value arrives as the member NAME
 * (`"InvitationPending"`, or `"ChangePassword, EnrollTotp"` for several) because the
 * backend serialises every enum by name. A bitwise AND against a string coerces to
 * `NaN`, and `NaN & anything` is `0`, so the test answers "no" for every input
 * without throwing or warning. That defect was live in this framework's own admin
 * user list: invited accounts rendered as "Locked" because the invitation badge
 * could never match.
 *
 * Use `hasFlag` from `@tnzi/core/utils`:
 * `hasFlag(user.pendingActions, PendingUserActions.InvitationPending, PendingUserActions)`
 */
export enum PendingUserActions {
  /** Owes nothing. */
  None = 0,

  // Blocking: the guard rejects the sign-in outright.
  /** Invited, waiting for the person to accept. No sign-in path will let it through. */
  InvitationPending = 1 << 0,

  // Obligations: signed in, but must finish this first.
  /** Must change the password before continuing (admin-issued temporary password, or expiry). */
  ChangePassword = 1 << 8,
  /** Must enrol an authenticator app first. */
  EnrollTotp = 1 << 9,
  /** Must confirm the email address first. */
  ConfirmEmail = 1 << 10,

  // Named composites, mirrored for WIRE PARITY only. When a value is exactly one
  // of these, the backend serialises it as the composite's name ("Obligations"
  // for an account that owes all three), and `parseFlags` can only expand a name
  // it knows: without these two rows that value parses to 0 and every badge is
  // skipped. Test against the single bits, not against these.
  /** Every bit that blocks the sign-in outright. */
  Blocking = InvitationPending,
  /** Every obligation bit. */
  Obligations = ChangePassword | EnrollTotp | ConfirmEmail,
}

/**
 * Complete the password change the sign-in demanded, and get tokens back.
 *
 * Carries no user identifier on purpose: which account to change is decided by the
 * token. Accepting a user id here would mean an anonymous endpoint takes "on whose
 * behalf" as a parameter.
 */
export interface CompletePasswordChangeDto {
  /** The temp token handed out with the pending-action challenge. Single use, 10 minutes. */
  tempToken: string;
  newPassword: string;
}

/**
 * The payload carried by a 403 `IDENTITY_PENDING_ACTIONS_REQUIRED` response.
 *
 * Same shape as the 2FA challenge: the front end stays inside the sign-in flow and
 * renders the matching step, rather than bouncing the user back to the login page.
 */
export interface PendingActionsChallengeDto {
  tempToken: string;
  /** Names of the outstanding obligations, e.g. `["ChangePassword"]`. */
  requiredActions: string[];
}

/** Complete an action that needs a code (enrol an authenticator, confirm an email). */
export interface CompletePendingActionCodeDto {
  tempToken: string;
  code: string;
}

/**
 * What is still owed and the material needed to discharge it.
 *
 * `totpSetup` is present when enrolment is owed - without it the front end knows
 * the user must enrol an authenticator but has no QR code to show.
 */
export interface PendingActionChallengeDto {
  requiredActions: string[];
  userName: string;
  totpSetup?: TotpSetupDto | null;
  maskedEmail?: string | null;
}

/**
 * Result of discharging one action.
 *
 * `completed: false` means other actions are still owed and the temp token is
 * **still usable** for the next one.
 */
export interface PendingActionResultDto {
  completed: boolean;
  remainingActions: string[];
  token?: TokenResultDto | null;
}

/**
 * Create an invitation: open the account, preset its roles, issue a one-time link.
 *
 * `userName` is chosen by the admin, not by the invitee - internal systems usually
 * have naming rules (staff number, corporate email), and the invitation email tells
 * the person what their username is.
 */
export interface CreateInvitationDto {
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  roleIds?: string[] | null;
  organizationId?: string | null;
  /** Admin-supplied form values, passed through to the app's acceptance handler verbatim. */
  profile?: Record<string, unknown> | null;
  /** Link lifetime in hours. Defaults to the server's `Identity:Invitation:LinkLifetimeHours` (7 days). */
  lifetimeHours?: number | null;
}

/**
 * A freshly issued invitation.
 *
 * `acceptUrl` is readable **only on this response** - the server stores nothing but
 * the token's hash. It is also the way to deliver the link yourself instead of
 * letting the app's `UserInvitedEvent` handler mail it.
 */
export interface InvitationDto {
  userId: string;
  userName: string;
  acceptUrl: string;
  expiresAt: Date | string;
}

/**
 * What the invitee sees when opening the link. Anonymous, so deliberately sparse:
 * contact addresses come back masked, because a leaked link must not leak an
 * employee's email address.
 */
export interface InvitationPreviewDto {
  userName: string;
  maskedEmail?: string | null;
  maskedPhoneNumber?: string | null;
  expiresAt: Date | string;
  profile?: Record<string, unknown> | null;
}

/** Accept an invitation. */
export interface AcceptInvitationDto {
  token: string;
  /** Whether a password is required is the consuming app's decision, not the framework's. */
  password?: string | null;
  /** App-defined form payload, passed through untouched. */
  payload?: Record<string, unknown> | null;
}

/**
 * Result of accepting.
 *
 * `completed: false` means the account is **still not active** and the token was not
 * consumed - the same link can be reopened to finish `remainingSteps`.
 */
export interface AcceptInvitationResultDto {
  completed: boolean;
  remainingSteps?: string[] | null;
  token?: TokenResultDto | null;
}
