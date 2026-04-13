<template>
  <div class="space-y-4">
    <!-- Auth Store -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">AuthStore</CardTitle>
        <CardDescription>Authentication state, token, and login status.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="grid grid-cols-2 gap-2 text-sm">
          <div>isAuthenticated: <span class="font-semibold">{{ authStore.isAuthenticated ? 'Yes' : 'No' }}</span></div>
          <div>Token: <span class="font-mono text-xs">{{ authStore.accessToken || '(none)' }}</span></div>
        </div>
        <div class="flex gap-2">
          <Button v-if="!authStore.isAuthenticated" size="sm" @click="handleLogin">
            Simulate Login
          </Button>
          <Button v-else size="sm" variant="destructive" @click="handleLogout">
            Logout
          </Button>
        </div>
      </CardContent>
    </Card>

    <!-- User Store -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">UserStore</CardTitle>
        <CardDescription>User profile and preferences.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div v-if="userStore.currentUser" class="grid grid-cols-2 gap-2 text-sm">
          <div>userName: <span class="font-semibold">{{ userStore.currentUser.userName }}</span></div>
          <div>nickname: <span class="font-semibold">{{ userStore.currentUser.nickname }}</span></div>
          <div>email: <span class="font-semibold">{{ userStore.currentUser.email }}</span></div>
          <div>roles: <span class="font-semibold">{{ userStore.currentUser.roles?.join(', ') }}</span></div>
        </div>
        <div v-else class="text-sm text-muted-foreground">No user loaded</div>
        <div class="flex gap-2">
          <Button size="sm" @click="handleLoadUserProfile">
            {{ userStore.currentUser ? 'Reload User' : 'Load User' }}
          </Button>
          <Button v-if="userStore.currentUser" size="sm" variant="outline" @click="handleClearUser">
            Clear
          </Button>
        </div>
      </CardContent>
    </Card>

    <!-- App Store -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">AppStore</CardTitle>
        <CardDescription>Global UI state, theme, and language.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="grid grid-cols-2 gap-2 text-sm">
          <div>theme: <span class="font-semibold">{{ appStore.theme }}</span></div>
          <div>language: <span class="font-semibold">{{ appStore.language }}</span></div>
        </div>
        <div class="flex flex-wrap gap-2">
          <Button size="sm" @click="appStore.setTheme(appStore.theme === 'dark' ? 'light' : 'dark')">
            Toggle Theme
          </Button>
          <Button size="sm" @click="switchLanguage">
            Toggle Language
          </Button>
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Button, Card, CardHeader, CardTitle, CardDescription, CardContent } from '@tnzi/ui';
import { useAuthStore, useUserStore, useAppStore, useMessage } from '@tnzi/ui';
import { demoMockUser } from '@/playground';
import { usePlaygroundI18n } from '../composables/usePlaygroundI18n';

const authStore = useAuthStore();
const userStore = useUserStore();
const appStore = useAppStore();
const message = useMessage();
const { setLocale } = usePlaygroundI18n();

const handleLogin = () => {
  authStore.$patch({
    accessToken: 'mock-token-' + Date.now(),
    isAuthenticated: true,
  });
  message.show('Login successful', 'success');
};

const handleLogout = () => {
  authStore.clearAuth();
  userStore.$patch({ currentUser: null });
  message.show('Logged out', 'info');
};

const switchLanguage = () => {
  const newLang = appStore.language === 'zh-CN' ? 'en-US' : 'zh-CN';
  appStore.setLanguage(newLang);
  setLocale(newLang === 'zh-CN' ? 'zh-CN' : 'en');
  message.show(`Language: ${newLang}`, 'success');
};

const handleLoadUserProfile = () => {
  userStore.$patch({
    currentUser: {
      id: demoMockUser.id,
      userName: demoMockUser.userName,
      nickname: demoMockUser.nickname,
      email: demoMockUser.email,
      roles: demoMockUser.roles,
    },
  });
  message.show('User profile loaded', 'success');
};

const handleClearUser = () => {
  userStore.$patch({ currentUser: null });
  message.show('User cleared', 'info');
};
</script>
