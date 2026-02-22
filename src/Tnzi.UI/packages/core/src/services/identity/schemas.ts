/**
 * Identity Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const genderSchema = z.enum(['0', '1', '2']).transform(Number).or(z.number().min(0).max(2));
export const oauthProviderSchema = z.enum(['Google', 'Microsoft', 'Facebook', 'Twitter', 'GitHub']);

// ============================================
// User Schemas
// ============================================

export const userDtoSchema = z.object({
  id: z.string(),
  userName: z.string().min(1),
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  nickname: z.string().nullable().optional(),
  avatar: z.string().url().nullable().optional(),
  avatarId: z.string().nullable().optional(),
  gender: z.number(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  bio: z.string().nullable().optional(),
  address: z.string().nullable().optional(),
  website: z.string().url().nullable().optional(),
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
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  password: z.string().min(6).max(100),
  nickname: z.string().max(50).optional(),
  gender: z.number().min(0).max(2).optional(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  bio: z.string().max(500).optional(),
  organizationId: z.string().nullable().optional(),
  roleNames: z.array(z.string()).optional(),
});

export const updateUserSchema = z.object({
  email: z.string().email().nullable().optional(),
  phoneNumber: z.string().nullable().optional(),
  nickname: z.string().max(50).nullable().optional(),
  avatarId: z.string().nullable().optional(),
  gender: z.number().min(0).max(2).optional(),
  birthday: z.union([z.date(), z.string()]).nullable().optional(),
  bio: z.string().max(500).nullable().optional(),
  address: z.string().max(200).nullable().optional(),
  website: z.string().url().nullable().optional(),
  organizationId: z.string().nullable().optional(),
  roleNames: z.array(z.string()).optional(),
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
  confirmPassword: z.string().min(1),
}).refine(data => data.newPassword === data.confirmPassword, {
  message: 'Passwords do not match',
  path: ['confirmPassword'],
});

export const resetPasswordSchema = z.object({
  email: z.string().email(),
  newPassword: z.string().min(6).max(100),
  confirmPassword: z.string().min(1),
  token: z.string().min(1),
}).refine(data => data.newPassword === data.confirmPassword, {
  message: 'Passwords do not match',
  path: ['confirmPassword'],
});

export const forgotPasswordSchema = z.object({
  email: z.string().email(),
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

// ============================================
// Role Schemas
// ============================================

export const roleDtoSchema = z.object({
  id: z.string(),
  name: z.string().min(1),
  normalizedName: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  isDefault: z.boolean(),
  isStatic: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const createRoleSchema = z.object({
  name: z.string().min(2).max(50),
  description: z.string().max(200).optional(),
  isDefault: z.boolean().optional(),
  permissionNames: z.array(z.string()).optional(),
});

export const updateRoleSchema = z.object({
  name: z.string().min(2).max(50).optional(),
  description: z.string().max(200).nullable().optional(),
  isDefault: z.boolean().optional(),
  permissionNames: z.array(z.string()).optional(),
});

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

export const captchaDtoSchema = z.object({
  captchaId: z.string().min(1),
  imageBase64: z.string().min(1),
  expirationSeconds: z.number().int().positive(),
});

export const oauthLoginSchema = z.object({
  provider: oauthProviderSchema,
  code: z.string().min(1),
  state: z.string().optional(),
});

// ============================================
// Organization Schemas
// ============================================

export const organizationDtoSchema: z.ZodTypeAny = z.object({
  id: z.string(),
  name: z.string().min(1),
  code: z.string().min(1),
  parentId: z.string().nullable().optional(),
  parentName: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  sortOrder: z.number().int(),
  memberCount: z.number().int().nonnegative(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
  children: z.array(z.lazy(() => organizationDtoSchema)).optional(),
});

export const createOrganizationSchema = z.object({
  name: z.string().min(1).max(100),
  code: z.string().min(1).max(50),
  parentId: z.string().nullable().optional(),
  description: z.string().max(500).optional(),
  sortOrder: z.number().int().optional(),
});

export const updateOrganizationSchema = z.object({
  name: z.string().min(1).max(100).optional(),
  code: z.string().min(1).max(50).optional(),
  parentId: z.string().nullable().optional(),
  description: z.string().max(500).nullable().optional(),
  sortOrder: z.number().int().optional(),
});
