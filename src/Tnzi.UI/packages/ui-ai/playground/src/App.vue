<script setup lang="ts">
/**
 * @tnzi/ui-ai playground — high-fidelity chat application.
 *
 * The playground is a fully interactive chat app shell that plays
 * 10 scripted mock scenarios exercising the ui-ai component library.
 * All state lives in usePlaygroundStore(); AppShell composes the
 * TCollapsibleSidebar / TCommandPalette / TSettingsDialog shell with
 * the MockChatEngine event player.
 */
import AppShell from './components/AppShell.vue'
</script>

<template>
  <AppShell />
</template>
