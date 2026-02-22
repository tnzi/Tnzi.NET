/**
 * Identity Module Types - User, Role, Session management
 * Aligned with Tnzi.NET backend Identity module
 */

import type { AuditedEntity, FullAuditedEntity } from '../../types/entities';
import type { PagedQueryDto, SortedPagedQueryDto } from '../../types/pagination';
import { Gender, OAuthProvider } from './metadata';

export { Gender, OAuthProvider };

// ============================================
// User Types
// ============================================

/**
 * User DTO - full user information
 */
export interface UserDto extends AuditedEntity<string> {
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  nickname?: string | null;
  avatar?: string | null;
  avatarId?: string | null;
  gender: Gender;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  isLockedOut: boolean;
  isEmailConfirmed: boolean;
  isPhoneNumberConfirmed: boolean;
  twoFactorEnabled: boolean;
  lockoutEnd?: Date | string | null;
  accessFailedCount: number;
  roles: string[];
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
 * User list item (simplified for lists)
 */
export interface UserListItemDto {
  id: string;
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  nickname?: string | null;
  avatar?: string | null;
  organizationName?: string | null;
  isLockedOut: boolean;
  creationTime: Date | string;
  roles: string[];
}

/**
 * Create user request
 */
export interface CreateUserDto {
  userName: string;
  email?: string | null;
  phoneNumber?: string | null;
  password: string;
  nickname?: string | null;
  gender?: Gender;
  birthday?: Date | string | null;
  bio?: string | null;
  organizationId?: string | null;
  roleNames?: string[];
}

/**
 * Update user request
 */
export interface UpdateUserDto {
  email?: string | null;
  phoneNumber?: string | null;
  nickname?: string | null;
  avatarId?: string | null;
  gender?: Gender;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
  organizationId?: string | null;
  roleNames?: string[];
}

/**
 * Update profile request (user self-service)
 */
export interface UpdateProfileDto {
  nickname?: string | null;
  avatarId?: string | null;
  gender?: Gender;
  birthday?: Date | string | null;
  bio?: string | null;
  address?: string | null;
  website?: string | null;
}

/**
 * Change password request
 */
export interface ChangePasswordDto {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

/**
 * Reset password request
 */
export interface ResetPasswordDto {
  email: string;
  newPassword: string;
  confirmPassword: string;
  token: string;
}

/**
 * Forgot password request
 */
export interface ForgotPasswordDto {
  email: string;
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

// ============================================
// Role Types
// ============================================

/**
 * Role DTO
 */
export interface RoleDto extends AuditedEntity<string> {
  name: string;
  normalizedName?: string | null;
  description?: string | null;
  isDefault: boolean;
  isStatic: boolean;
}

/**
 * Role list item
 */
export interface RoleListItemDto {
  id: string;
  name: string;
  description?: string | null;
  isDefault: boolean;
  isStatic: boolean;
  userCount: number;
  creationTime: Date | string;
}

/**
 * Create role request
 */
export interface CreateRoleDto {
  name: string;
  description?: string | null;
  isDefault?: boolean;
  permissionNames?: string[];
}

/**
 * Update role request
 */
export interface UpdateRoleDto {
  name?: string;
  description?: string | null;
  isDefault?: boolean;
  permissionNames?: string[];
}

/**
 * Role list query parameters
 */
export interface RoleListQueryDto extends PagedQueryDto {
  keyword?: string;
  isDefault?: boolean;
  isStatic?: boolean;
}

// ============================================
// Session Types
// ============================================

/**
 * User session DTO
 */
export interface UserSessionDto extends FullAuditedEntity<string> {
  userId: string;
  deviceInfo?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  lastActivityTime: Date | string;
  isRevoked: boolean;
  revokedAt?: Date | string | null;
  expiresAt?: Date | string | null;
}

/**
 * Session list query parameters
 */
export interface SessionListQueryDto extends PagedQueryDto {
  userId?: string;
  isRevoked?: boolean;
  startDate?: Date | string;
  endDate?: Date | string;
}

// ============================================
// Organization Types
// ============================================

/**
 * Organization DTO
 */
export interface OrganizationDto extends AuditedEntity<string> {
  name: string;
  code: string;
  parentId?: string | null;
  parentName?: string | null;
  description?: string | null;
  sortOrder: number;
  memberCount: number;
  children?: OrganizationDto[];
}

/**
 * Organization list item
 */
export interface OrganizationListItemDto {
  id: string;
  name: string;
  code: string;
  parentId?: string | null;
  memberCount: number;
  sortOrder: number;
}

/**
 * Create organization request
 */
export interface CreateOrganizationDto {
  name: string;
  code: string;
  parentId?: string | null;
  description?: string | null;
  sortOrder?: number;
}

/**
 * Update organization request
 */
export interface UpdateOrganizationDto {
  name?: string;
  code?: string;
  parentId?: string | null;
  description?: string | null;
  sortOrder?: number;
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
 * Login response
 */
export interface LoginResultDto {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserProfile;
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
 * Captcha payload DTO
 */
export interface CaptchaDto {
  captchaId: string;
  imageBase64: string;
  expirationSeconds: number;
}

/**
 * OAuth login request
 */
export interface OAuthLoginDto {
  provider: OAuthProvider;
  code: string;
  state?: string;
}

/**
 * OAuth callback result
 */
export interface OAuthCallbackResultDto {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserProfile;
  isNewUser: boolean;
}
