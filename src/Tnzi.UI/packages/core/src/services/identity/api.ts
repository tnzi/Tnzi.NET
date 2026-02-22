/**
 * Identity Module API - Auth, User, Role, and Organization management
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  LoginDto,
  LoginResultDto,
  RegisterDto,
  RefreshTokenDto,
  ChangePasswordDto,
  UserDto,
  CreateUserDto,
  UpdateUserDto,
  UserListQueryDto,
  RoleDto,
  CreateRoleDto,
  UpdateRoleDto,
  RoleListQueryDto,
  OrganizationDto,
  CreateOrganizationDto,
  UpdateOrganizationDto,
  CaptchaDto,
} from './types';

// Routes aligned with backend controllers
const AUTH_BASE = '/auth';
const PROFILE_BASE = '/users/profile';
const ADMIN_USER_BASE = '/admin/users';
const ADMIN_ROLE_BASE = '/admin/roles';
const ADMIN_ORG_BASE = '/admin/organizations';

/**
 * Auth API
 */
export function useAuthApi(client: HttpClient) {
  return {
    /** Login */
    login: (data: LoginDto) =>
      client.post<string>(`${AUTH_BASE}/login`, data),

    /** Login with refresh token */
    loginWithRefreshToken: (data: LoginDto) =>
      client.post<LoginResultDto>(`${AUTH_BASE}/login-with-refresh-token`, data),

    /** Refresh token */
    refreshToken: (data: RefreshTokenDto) =>
      client.post<LoginResultDto>(`${AUTH_BASE}/refresh-token`, data),

    /** Logout */
    logout: (userId: string) =>
      client.post<string>(`${AUTH_BASE}/logout`, userId),

    /** Register */
    register: (data: RegisterDto) =>
      client.post<LoginResultDto>(`${AUTH_BASE}/register`, data),

    /** Get Captcha Image and set ID in header */
    getCaptcha: (purpose: 'login' | 'register') =>
      client.download(`${AUTH_BASE}/captcha/${purpose}`),

    /** Get Captcha JSON (base64) */
    getCaptchaJson: (purpose: 'login' | 'register') =>
      client.get<CaptchaDto>(`${AUTH_BASE}/captcha/${purpose}/json`),
  };
}

/**
 * User Profile API
 */
export function useProfileApi(client: HttpClient) {
  return {
    /** Get current user profile */
    get: () =>
      client.get<UserDto>(PROFILE_BASE),

    /** Update profile (self) */
    update: (data: UpdateUserDto) =>
      client.put<UserDto>(PROFILE_BASE, data),

    /** Change password */
    changePassword: (data: ChangePasswordDto) =>
      client.post<void>(`${PROFILE_BASE}/change-password`, data),
  };
}

/**
 * Admin User Management API
 */
export function useAdminUserApi(client: HttpClient) {
  return {
    /** Get user list */
    getList: (data?: UserListQueryDto) =>
      client.post<PagedList<UserDto>>(`${ADMIN_USER_BASE}/list`, data ?? {}),

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
  };
}

/**
 * Admin Role Management API
 */
export function useAdminRoleApi(client: HttpClient) {
  return {
    /** Get role list (GET) */
    getList: (params?: RoleListQueryDto) =>
      client.get<PagedList<RoleDto>>(ADMIN_ROLE_BASE, { params }),

    /** Get role by ID */
    getById: (id: string) =>
      client.get<RoleDto>(`${ADMIN_ROLE_BASE}/${id}`),

    /** Create role */
    create: (data: CreateRoleDto) =>
      client.post<RoleDto>(ADMIN_ROLE_BASE, data),

    /** Update role */
    update: (id: string, data: UpdateRoleDto) =>
      client.put<RoleDto>(`${ADMIN_ROLE_BASE}/${id}`, data),

    /** Delete role */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_ROLE_BASE}/${id}`),
  };
}

/**
 * Admin Organization Management API
 */
export function useAdminOrganizationApi(client: HttpClient) {
  return {
    /** Get organization tree */
    getList: () =>
      client.get<OrganizationDto[]>(`${ADMIN_ORG_BASE}/tree`),

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
  };
}
