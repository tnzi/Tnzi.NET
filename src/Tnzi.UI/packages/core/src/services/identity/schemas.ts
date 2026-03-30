/**
 * Identity Module Zod Schemas
 * Aligned with Tnzi.NET backend Identity module DTOs
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const genderSchema = z.enum(['0', '1', '2']).transform(Number).or(z.number().min(0).max(2));
export const oauthProviderSchema = z.enum(['Google', 'Microsoft', 'Facebook', 'Twitter', 'GitHub']);
export const twoFactorTypeSchema = z.number().min(1).max(2);
export const loginStatusSchema = z.number().min(1).max(2);
export const passwordStrengthLevelSchema = z.number().min(0).max(4);

// ============================================
// Auth Schemas
// ============================================

export const loginSchema = z.object({
  userName: z.string().min(1),
  password: z.string().min(1),
  captchaId: z.string().optional(),
  captchaCode: z.string().optional(),
});

export const registerSchema = z.object({
  userName: z.string().min(2).max(50).optional(),
  email: z.string().email(),
  password: z.string().min(6).max(100),
  captchaId: z.string().optional(),
  captchaCode: z.string().optional(),
  firstName: z.string().max(50).optional(),
  lastName: z.string().max(50).optional(),
});

export const refreshTokenSchema = z.object({
  refreshToken: z.string().min(1),
});

export const forgotPasswordSchema = z.object({
  email: z.string().email(),
});

export const resetPasswordSchema = z.object({
  email: z.string().email(),
  token: z.string().min(1),
  newPassword: z.string().min(6).max(100),
});

export const captchaDtoSchema = z.object({
  captchaId: z.string().min(1),
  imageBase64: z.string().min(1),
  expirationSeconds: z.number().int().positive(),
});

export const resendEmailConfirmationSchema = z.object({
  userId: z.string().nullable().optional(),
  email: z.string().email().nullable().optional(),
});

// ============================================
// Quick Register Schemas
// ============================================

export const sendQuickRegisterCodeSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
});

export const quickRegisterSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  code: z.string().min(1),
  userName: z.string().nullable().optional(),
  firstName: z.string().nullable().optional(),
  lastName: z.string().nullable().optional(),
});

export const setPasswordSchema = z.object({
  userId: z.string().min(1),
  token: z.string().min(1),
  password: z.string().min(6).max(100),
});

// ============================================
// Code Login Schemas
// ============================================

export const sendCodeLoginCodeSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  type: twoFactorTypeSchema,
});

export const codeLoginSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  code: z.string().min(1),
  type: twoFactorTypeSchema,
});

// ============================================
// Password Recovery Schemas
// ============================================

export const sendPasswordRecoveryCodeSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  type: twoFactorTypeSchema,
});

export const resetPasswordByCodeSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  code: z.string().min(1),
  newPassword: z.string().min(6).max(100),
  type: twoFactorTypeSchema,
});

// ============================================
// Two-Factor Auth Schemas
// ============================================

export const sendTwoFactorCodeSchema = z.object({
  tempToken: z.string().min(1),
  type: twoFactorTypeSchema,
});

export const verifyTwoFactorSchema = z.object({
  tempToken: z.string().min(1),
  code: z.string().min(1),
  type: twoFactorTypeSchema,
});

export const enableTwoFactorSchema = z.object({
  type: twoFactorTypeSchema,
});

export const enableTotpSchema = z.object({
  verificationCode: z.string().min(1),
});

// ============================================
// User Schemas
// ============================================

export const userDtoSchema = z.object({
  id: z.string(),
  userName: z.string().min(1),
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  nickname: z.string().nullable().optional(),
  avatar: z.string().nullable().optional(),
  avatarId: z.string().nullable().optional(),
  gender: z.number(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  bio: z.string().nullable().optional(),
  address: z.string().nullable().optional(),
  website: z.string().nullable().optional(),
  organizationId: z.string().nullable().optional(),
  organizationName: z.string().nullable().optional(),
  isLockedOut: z.boolean(),
  isEmailConfirmed: z.boolean(),
  isPhoneNumberConfirmed: z.boolean(),
  twoFactorEnabled: z.boolean(),
  lockoutEnd: z.union([z.date(), z.string()]).nullable().optional(),
  accessFailedCount: z.number().int().nonnegative(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
  roles: z.array(z.string()),
});

export const createUserSchema = z.object({
  userName: z.string().min(2).max(50),
  password: z.string().min(6).max(100),
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  nickname: z.string().max(50).optional(),
  organizationId: z.string().nullable().optional(),
  roleIds: z.array(z.string()).optional(),
});

export const updateUserSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  nickname: z.string().max(50).nullable().optional(),
  avatarUrl: z.string().nullable().optional(),
  avatarId: z.string().nullable().optional(),
  gender: z.number().min(0).max(2).optional(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  bio: z.string().max(500).nullable().optional(),
  address: z.string().max(200).nullable().optional(),
  website: z.string().nullable().optional(),
  organizationId: z.string().nullable().optional(),
  roleIds: z.array(z.string()).optional(),
});

export const updateProfileSchema = updateUserSchema.pick({
  nickname: true,
  avatarId: true,
  gender: true,
  birthday: true,
  bio: true,
  address: true,
  website: true,
});

export const changePasswordSchema = z.object({
  currentPassword: z.string().min(1),
  newPassword: z.string().min(6).max(100),
});

export const resetPasswordByAdminSchema = z.object({
  newPassword: z.string().min(6).max(100),
});

export const lockUserSchema = z.object({
  lockoutEnd: z.union([z.date(), z.string()]).nullable().optional(),
  reason: z.string().nullable().optional(),
});

export const userListQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  keyword: z.string().optional(),
  organizationId: z.string().optional(),
  roleId: z.string().optional(),
  isLockedOut: z.boolean().optional(),
  isEmailConfirmed: z.boolean().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

export const deactivateAccountSchema = z.object({
  reason: z.string().max(500).nullable().optional(),
});

// ============================================
// User Detail Schemas
// ============================================

export const createUserDetailSchema = z.object({
  firstName: z.string().nullable().optional(),
  lastName: z.string().nullable().optional(),
  nickname: z.string().nullable().optional(),
  avatarUrl: z.string().nullable().optional(),
  avatarId: z.string().nullable().optional(),
  gender: z.number().min(0).max(2).optional(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  address: z.string().nullable().optional(),
  bio: z.string().nullable().optional(),
  website: z.string().nullable().optional(),
});

// ============================================
// Change Email/Phone Schemas
// ============================================

export const sendChangeVerificationCodeSchema = z.object({
  newAddress: z.string().min(1),
});

export const changeEmailSchema = z.object({
  newEmail: z.string().email(),
  code: z.string().min(1),
});

export const changePhoneNumberSchema = z.object({
  newPhoneNumber: z.string().min(1),
  code: z.string().min(1),
});

// ============================================
// Role Schemas
// ============================================

export const roleDtoSchema = z.object({
  id: z.string(),
  name: z.string().min(1),
  normalizedName: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  isSystem: z.boolean(),
  isDefault: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
});

export const createRoleSchema = z.object({
  name: z.string().min(1).max(256),
  description: z.string().max(500).optional(),
  isDefault: z.boolean().optional(),
});

export const updateRoleSchema = z.object({
  name: z.string().min(1).max(256),
  description: z.string().max(500).nullable().optional(),
  isDefault: z.boolean().optional(),
});

export const roleListQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  keyword: z.string().optional(),
  isSystem: z.boolean().optional(),
  isDefault: z.boolean().optional(),
});

// ============================================
// Organization Schemas
// ============================================

export const organizationDtoSchema: z.ZodTypeAny = z.object({
  id: z.string(),
  name: z.string().min(1),
  code: z.string().nullable().optional(),
  remark: z.string().nullable().optional(),
  parentId: z.string().nullable().optional(),
  sortOrder: z.number().int(),
  isEnabled: z.boolean(),
  path: z.string().nullable().optional(),
  level: z.number().int(),
  creationTime: z.union([z.date(), z.string()]),
  children: z.array(z.lazy(() => organizationDtoSchema)).optional(),
});

export const createOrganizationSchema = z.object({
  name: z.string().min(1).max(100),
  code: z.string().max(50).nullable().optional(),
  remark: z.string().max(500).nullable().optional(),
  parentId: z.string().nullable().optional(),
  sortOrder: z.number().int().optional(),
});

export const updateOrganizationSchema = z.object({
  name: z.string().min(1).max(100),
  code: z.string().max(50).nullable().optional(),
  remark: z.string().max(500).nullable().optional(),
  sortOrder: z.number().int().optional(),
  isEnabled: z.boolean().optional(),
});

export const moveOrganizationSchema = z.object({
  newParentId: z.string().nullable().optional(),
});

// ============================================
// Tenant Schemas
// ============================================

export const createTenantSchema = z.object({
  name: z.string().min(1).max(128),
  code: z.string().max(64).nullable().optional(),
  isEnabled: z.boolean().optional(),
  expiredAt: z.union([z.date(), z.string()]).nullable().optional(),
  remark: z.string().max(500).nullable().optional(),
});

export const updateTenantSchema = z.object({
  name: z.string().min(1).max(128),
  code: z.string().max(64).nullable().optional(),
  isEnabled: z.boolean().optional(),
  expiredAt: z.union([z.date(), z.string()]).nullable().optional(),
  remark: z.string().max(500).nullable().optional(),
});

export const tenantQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  keyword: z.string().optional(),
  isEnabled: z.boolean().optional(),
});

// ============================================
// OAuth Schemas
// ============================================

export const oauthLoginSchema = z.object({
  provider: oauthProviderSchema,
  code: z.string().min(1),
  state: z.string().optional(),
});
