<script setup lang="ts">
import { showToast } from 'vant';
import { demoMockUser } from '../data';
import { useAuthStore, useUserStore, useAppStore } from '../stores';
import { usePlaygroundI18n } from '../composables/usePlaygroundI18n';

const authStore = useAuthStore();
const userStore = useUserStore();
const appStore = useAppStore();
const { locale, setLocale } = usePlaygroundI18n();

// Auth 操作
const handleLogin = () => {
  authStore.setToken('mock-token-' + Date.now());
  showToast('Login successful');
};
const handleLogout = () => {
  authStore.clearAuth();
  userStore.clearUser();
  showToast('Logged out');
};

// User 操作
const handleLoadUser = () => {
  userStore.loadMockUser();
  showToast('User loaded');
};
const handleClearUser = () => {
  userStore.clearUser();
  showToast('User cleared');
};

// App 操作
const handleToggleTheme = () => {
  const next = appStore.theme === 'dark' ? 'light' : 'dark';
  appStore.setTheme(next);
  showToast(`Theme: ${next}`);
};
const handleToggleLang = () => {
  const next = appStore.language === 'zh-CN' ? 'en-US' : 'zh-CN';
  appStore.setLanguage(next);
  // Sync i18n and Vant locale
  setLocale(next === 'zh-CN' ? 'zh-CN' : 'en');
  showToast(`Language: ${next}`);
};
</script>

<template>
  <div class="stores-section">
    <!-- Auth Store -->
    <van-cell-group inset title="useAuthStore">
      <van-cell title="isAuthenticated" :value="authStore.isAuthenticated ? 'Yes' : 'No'" />
      <van-cell
        title="accessToken"
        :value="authStore.accessToken ? authStore.accessToken.slice(0, 20) + '...' : 'N/A'"
        :label="authStore.accessToken || undefined"
      />
      <div class="btn-grid">
        <van-button
          v-if="!authStore.isAuthenticated"
          type="primary"
          size="small"
          @click="handleLogin"
        >
          Mock Login
        </van-button>
        <van-button
          v-else
          type="danger"
          size="small"
          @click="handleLogout"
        >
          Logout
        </van-button>
      </div>
    </van-cell-group>

    <!-- User Store -->
    <van-cell-group inset title="useUserStore">
      <template v-if="userStore.currentUser">
        <van-cell title="userName" :value="userStore.currentUser.userName" />
        <van-cell title="nickname" :value="userStore.currentUser.nickname" />
        <van-cell title="email" :value="userStore.currentUser.email" />
        <van-cell title="roles" :value="userStore.currentUser.roles.join(', ')" />
      </template>
      <van-cell v-else title="currentUser" value="Not loaded" />
      <div class="btn-grid">
        <van-button type="primary" size="small" @click="handleLoadUser">Load Mock User</van-button>
        <van-button size="small" plain @click="handleClearUser">Clear</van-button>
      </div>
    </van-cell-group>

    <!-- App Store -->
    <van-cell-group inset title="useAppStore">
      <van-cell title="theme" :value="appStore.theme" />
      <van-cell title="language" :value="appStore.language" />
      <van-cell title="i18n locale" :value="locale" />
      <div class="btn-grid">
        <van-button type="primary" size="small" @click="handleToggleTheme">
          Toggle Theme
        </van-button>
        <van-button type="primary" size="small" @click="handleToggleLang">
          Toggle Language
        </van-button>
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.stores-section {
  padding-bottom: 8px;
}

.btn-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 12px;
}
</style>
