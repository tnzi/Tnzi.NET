/**
 * Playground 本地 Pinia stores
 * 统一 auth / user / app 三个 store 的接口。
 */

import { defineStore } from 'pinia';
import { demoMockUser } from '@tnzi/core/playground';

export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: null as string | null,
  }),
  getters: {
    isAuthenticated: (state) => !!state.accessToken,
  },
  actions: {
    setToken(token: string) {
      this.accessToken = token;
    },
    clearAuth() {
      this.accessToken = null;
    },
  },
});

export const useUserStore = defineStore('user', {
  state: () => ({
    currentUser: null as typeof demoMockUser | null,
  }),
  actions: {
    setUser(user: typeof demoMockUser) {
      this.currentUser = user;
    },
    clearUser() {
      this.currentUser = null;
    },
  },
});

export const useAppStore = defineStore('app', {
  state: () => ({
    theme: 'light' as 'light' | 'dark',
    language: 'zh-CN' as string,
  }),
  actions: {
    setTheme(theme: 'light' | 'dark') {
      this.theme = theme;
    },
    setLanguage(language: string) {
      this.language = language;
    },
  },
});
