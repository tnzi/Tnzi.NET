/**
 * Identity Module Types - User, Role, Session, Organization, Auth management
 * Aligned with Tnzi.NET backend Identity module
 */

import type { PagedQueryDto, SortedPagedQueryDto } from '../../types/pagination';
import { Gender, OAuthProvider, TwoFactorType, PasswordStrengthLevel, AbnormalLoginType, AbnormalLoginAction, LoginStatus } from './metadata';

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
   * User name — populated by the global session list (GET /admin/sessions,
   * batch-joined against the user table); the per-user endpoints leave it
   * null (the caller already knows the user).
   */
  userName?: string | null;
  deviceInfo?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  creationTime: Date | string;
  lastActivityTime: Date | string;
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
 * Active user summary — backs the admin "active users" picker on the session
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
 * Login request
 */
export interface LoginDto {
  userName: string;
  password: string;
  captchaId?: string;
  captchaCode?: string;
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
  firstName?: string;
  lastName?: string;
}

/**
 * Refresh token request
 */
export interface RefreshTokenDto {
  refreshToken: string;
}

/**
 * Forgot password request
 */
export interface ForgotPasswordDto {
  email: string;
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
 * Captcha payload DTO
 */
export interface CaptchaDto {
  captchaId: string;
  imageBase64: string;
  expirationSeconds: number;
}

/**
 * Resend email confirmation request
 */
export interface ResendEmailConfirmationDto {
  userId?: string | null;
  email?: string | null;
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
 * 2FA status DTO
 */
export interface TwoFactorStatusDto {
  isEnabled: boolean;
  supportedTypes: TwoFactorType[];
  currentType?: TwoFactorType | null;
  isTotpEnabled: boolean;
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
