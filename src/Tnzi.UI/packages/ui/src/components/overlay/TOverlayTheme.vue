<template>
  <!-- Renderless (`abstract`): resets the naive theme of a teleported overlay
       to the GLOBAL light/dark mode, so a modal / drawer opened from a page
       whose "Card / List" surface is dark does not inherit the content area's
       inner dark theme through the Teleport. Wrap a hand-rolled `NModal` /
       `NDrawer` in this (the TModalShell / TDrawerShell primitives already do
       it internally). See headless/useOverlayTheme. -->
  <NConfigProvider abstract :theme="overlayTheme" :theme-overrides="overlayOverrides">
    <slot />
  </NConfigProvider>
</template>

<script setup lang="ts">
import { NConfigProvider } from 'naive-ui'
import { useOverlayTheme, useOverlayThemeOverrides } from '../../headless/theme/useOverlayTheme'

const overlayTheme = useOverlayTheme()
const overlayOverrides = useOverlayThemeOverrides()
</script>
