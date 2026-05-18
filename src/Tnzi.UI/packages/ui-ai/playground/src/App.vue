<script setup lang="ts">
/**
 * @tnzi/ui-ai playground — living examples of the package's components.
 *
 * `AppShell.vue` is a thin consumer of `@tnzi/ui-ai/chat`'s `TChatApp`
 * (the canonical chat shell). It contributes only the playground-specific
 * bits: the scenario picker sidebar, replay controls, command palette
 * and settings dialog wiring — all driven by `usePlaygroundStore()` and
 * a scripted `MockChatEngine`.
 */
import AppShell from './components/AppShell.vue'
</script>

<template>
  <AppShell />
</template>
