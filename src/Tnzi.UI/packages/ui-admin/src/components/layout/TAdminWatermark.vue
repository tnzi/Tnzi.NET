<script setup lang="ts">
import { computed } from 'vue'
import { NWatermark } from 'naive-ui'
import { useAdminThemeStore } from '../../stores/useAdminThemeStore'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'

/**
 * Floating watermark overlay reading config from `useAdminThemeStore`.
 *
 * Renders nothing when watermark is disabled - zero footprint by default.
 * When enabled the text combines a user-defined string with optional
 * username + date suffixes.
 */
const themeStore = useAdminThemeStore()
const authStore = useAdminAuthStore()

const composedContent = computed<string>(() => {
  const parts: string[] = []
  if (themeStore.watermark.text) parts.push(themeStore.watermark.text)
  const displayName =
    authStore.userInfo?.displayName ?? authStore.userInfo?.username
  if (themeStore.watermark.includeUserName && displayName) {
    parts.push(displayName)
  }
  if (themeStore.watermark.includeDate) {
    parts.push(new Date().toISOString().slice(0, 10))
  }
  return parts.join(' · ')
})
</script>

<template>
  <NWatermark
    v-if="themeStore.watermark.enabled && composedContent"
    :content="composedContent"
    cross
    fullscreen
    selectable
    :width="200"
    :height="100"
    :x-offset="12"
    :y-offset="60"
    :rotate="-15"
    :font-size="themeStore.watermark.fontSize"
    :font-color="`rgba(128, 128, 128, ${themeStore.watermark.opacity})`"
  />
</template>
