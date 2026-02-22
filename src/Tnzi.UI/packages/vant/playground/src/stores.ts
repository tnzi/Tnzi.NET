/**
 * Playground Pinia Stores
 *
 * 轻量级本地 store，不依赖后端 API，仅用于演示状态管理。
 */

import { defineStore } from 'pinia';
import { demoMockUser } from '@tnzi/core/playground';

// ============================================
// Auth Store
// ============================================

export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: '' as string,
    isAuthenticated: false,
  }),
  actions: {
    setToken(token: string) {
      this.accessToken = token;
      this.isAuthenticated = true;
    },
    clearAuth() {
      this.accessToken = '';
      this.isAuthenticated = false;
    },
  },
});

// ============================================
// User Store
// ============================================

export interface MockUser {
  id: string;
  userName: string;
  nickname: string;
  email: string;
  roles: string[];
}

export const useUserStore = defineStore('user', {
  state: () => ({
    currentUser: null as MockUser | null,
  }),
  actions: {
    setUser(user: MockUser) {
      this.currentUser = user;
    },
    clearUser() {
      this.currentUser = null;
    },
    loadMockUser() {
      this.currentUser = { ...demoMockUser };
    },
  },
});

// ============================================
// App Store
// ============================================

export const useAppStore = defineStore('app', {
  state: () => ({
    theme: 'light' as string,
    language: 'zh-CN' as string,
  }),
  actions: {
    setTheme(theme: string) {
      this.theme = theme;
    },
    setLanguage(language: string) {
      this.language = language;
    },
  },
});
