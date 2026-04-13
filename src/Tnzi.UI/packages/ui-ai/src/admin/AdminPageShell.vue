<script setup lang="ts">
/**
 * AdminPageShell -- Shared layout shell for admin pages
 *
 * Provides consistent padding, scroll area, and loading state.
 */

import { ScrollArea } from '../primitives';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

defineProps<{
  isLoading?: boolean;
}>();
</script>

<template>
  <div class="p-6 space-y-4">
    <slot name="header" />
    <slot name="toolbar" />
    <ScrollArea class="max-h-[calc(100vh-200px)]">
      <div v-if="isLoading" class="py-12 text-center text-muted-foreground">
        {{ t.common.loading }}
      </div>
      <div v-else>
        <slot />
      </div>
    </ScrollArea>
  </div>
</template>
