<template>
  <n-config-provider
    :theme="currentNaiveTheme"
    :theme-overrides="themeOverrides"
    :locale="naiveLocale"
    :date-locale="naiveDateLocale"
  >
    <n-loading-bar-provider>
      <n-message-provider>
        <n-dialog-provider>
          <n-notification-provider>
            <AppContent />
          </n-notification-provider>
        </n-dialog-provider>
      </n-message-provider>
    </n-loading-bar-provider>
  </n-config-provider>
</template>

<script setup lang="ts">
import AppContent from './AppContent.vue';
import { usePlaygroundTheme } from './composables/usePlaygroundTheme';
import { usePlaygroundI18n } from './composables/usePlaygroundI18n';

const { currentNaiveTheme, themeOverrides } = usePlaygroundTheme();
const { naiveLocale, naiveDateLocale } = usePlaygroundI18n();
</script>
