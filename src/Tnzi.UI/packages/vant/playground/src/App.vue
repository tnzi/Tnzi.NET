<script setup lang="ts">
import { usePlaygroundTheme } from './composables/usePlaygroundTheme';
import { useMobileViewport } from '../../src/composables/useMobileViewport';
import DesktopWrapper from './components/DesktopWrapper.vue';
import MobileApp from './components/MobileApp.vue';

const { vantThemeVars, isDark } = usePlaygroundTheme();
const { isMobile } = useMobileViewport();
</script>

<template>
  <van-config-provider :theme="isDark ? 'dark' : 'light'" :theme-vars="vantThemeVars">
    <DesktopWrapper v-if="!isMobile">
      <MobileApp />
    </DesktopWrapper>
    <MobileApp v-else />
  </van-config-provider>
</template>
