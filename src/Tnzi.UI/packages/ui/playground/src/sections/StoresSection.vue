<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">State Stores</h1>
      <p class="section-desc">Pinia-based stores for authentication, user profile, and application settings.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">AuthStore</h2>
      <PreviewBox :no-badge :full-width>
        <n-space vertical :size="8">
          <n-text>isAuthenticated: {{ authStore.isAuthenticated ? 'Yes' : 'No' }}</n-text>
          <n-text>accessToken: {{ authStore.accessToken ?? '(none)' }}</n-text>
          <n-space>
            <n-button
              v-if="!authStore.isAuthenticated"
              type="primary"
              size="small"
              @click="handleLogin"
            >
              Simulate Login
            </n-button>
            <n-button v-else type="error" size="small" @click="handleLogout">Logout</n-button>
          </n-space>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">UserStore</h2>
      <PreviewBox :no-badge :full-width>
        <n-space vertical :size="8">
          <template v-if="userStore.currentUser">
            <n-text>userName: {{ userStore.currentUser.userName }}</n-text>
            <n-text>nickname: {{ userStore.currentUser.nickname }}</n-text>
            <n-text>email: {{ userStore.currentUser.email }}</n-text>
            <n-text>roles: {{ userStore.currentUser.roles.join(', ') }}</n-text>
          </template>
          <n-text v-else depth="3">No user loaded</n-text>
          <n-space>
            <n-button size="small" @click="handleLoadUser">
              {{ userStore.currentUser ? 'Reload User' : 'Load User' }}
            </n-button>
            <n-button v-if="userStore.currentUser" size="small" @click="userStore.clearUser()">
              Clear
            </n-button>
          </n-space>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">AppStore</h2>
      <PreviewBox :no-badge :full-width>
        <n-space vertical :size="8">
          <n-text>theme: {{ appStore.theme }}</n-text>
          <n-text>language: {{ appStore.language }}</n-text>
          <n-space>
            <n-button
              size="small"
              @click="appStore.setTheme(appStore.theme === 'dark' ? 'light' : 'dark')"
            >
              Toggle Theme
            </n-button>
            <n-button
              size="small"
              @click="handleToggleLanguage"
            >
              Toggle Language
            </n-button>
          </n-space>
        </n-space>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMessage } from 'naive-ui';
import { demoMockUser } from '../data/index';
import { createMessageAdapter } from '@tnzi/ui';
import type { Locale } from '@tnzi/core/adapters/i18n';
import PreviewBox from '../components/PreviewBox.vue';
import { useAuthStore, useUserStore, useAppStore } from '../stores';
import { usePlaygroundI18n } from '../composables/usePlaygroundI18n';

const message = useMessage();
const msgAdapter = createMessageAdapter(message);

const { setLocale } = usePlaygroundI18n();

const authStore = useAuthStore();
const userStore = useUserStore();
const appStore = useAppStore();

function handleLogin() {
  authStore.setToken('mock-token-' + Date.now());
  msgAdapter.success('Login succeeded');
}

function handleLogout() {
  authStore.clearAuth();
  userStore.clearUser();
  msgAdapter.info('Logged out');
}

function handleToggleLanguage() {
  const newLang = appStore.language === 'zh-CN' ? 'en' : 'zh-CN';
  appStore.setLanguage(newLang === 'en' ? 'en-US' : newLang);
  setLocale(newLang as Locale);
}

function handleLoadUser() {
  userStore.setUser({ ...demoMockUser });
  msgAdapter.success('User loaded');
}
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
</style>
