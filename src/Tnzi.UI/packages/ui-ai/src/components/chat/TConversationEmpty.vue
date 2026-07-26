<script setup lang="ts">
/**
 * TConversationEmpty - Empty chat state
 *
 * Displayed when there are no messages in the conversation.
 * Shows a centered icon, title, description, and a slot for suggestions.
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';

const props = withDefaults(defineProps<{
  title?: string;
  description?: string;
  /** Iconify icon name. */
  icon?: string;
}>(), {
  icon: 'lucide:message-square-plus',
});

const t = useAiI18n();
</script>

<template>
  <div class="flex size-full flex-col items-center justify-center gap-4 px-4 text-center">
    <!-- Default slot replaces entire content when provided -->
    <template v-if="$slots.default">
      <slot />
    </template>

    <template v-else>
      <div class="flex size-16 items-center justify-center rounded-full bg-muted">
        <Icon :icon="props.icon" class="size-8 text-muted-foreground" />
      </div>

      <div class="space-y-2">
        <h3 class="text-lg font-semibold text-foreground">
          {{ title ?? t.chat.emptyState }}
        </h3>
        <p
          v-if="description ?? t.chat.placeholder"
          class="max-w-sm text-sm text-muted-foreground"
        >
          {{ description ?? t.chat.placeholder }}
        </p>
      </div>

      <!-- Suggestions slot appends below default content -->
      <div v-if="$slots.suggestions" class="mt-4 w-full max-w-lg">
        <slot name="suggestions" />
      </div>
    </template>
  </div>
</template>
