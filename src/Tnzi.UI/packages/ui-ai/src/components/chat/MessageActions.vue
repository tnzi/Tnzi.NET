<script setup lang="ts">
/**
 * MessageActions — Message action bar
 *
 * Shows copy, regenerate, and edit actions below assistant messages.
 * Appears on hover (opacity-0 → group-hover:opacity-100).
 * Copy shows a check icon for 2 seconds after clicking.
 */

import { NButton, NTooltip } from 'naive-ui';
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
withDefaults(defineProps<{
  showCopy?: boolean;
  showRegenerate?: boolean;
  showEdit?: boolean;
}>(), {
  showCopy: true,
  showRegenerate: true,
  showEdit: false,
});

const emit = defineEmits<{
  copy: [];
  regenerate: [];
  edit: [];
}>();

const t = useAiI18n();
const hasCopied = ref(false);
let copyTimeout: ReturnType<typeof setTimeout> | null = null;

function handleCopy(): void {
  emit('copy');
  hasCopied.value = true;
  if (copyTimeout) clearTimeout(copyTimeout);
  copyTimeout = setTimeout(() => {
    hasCopied.value = false;
  }, 2000);
}
</script>

<template>
  <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
    <!-- Copy -->
    <NTooltip v-if="showCopy">
      <template #trigger>
        <NButton quaternary size="tiny" @click="handleCopy">
          <template #icon>
            <Icon :icon="hasCopied ? 'lucide:check' : 'lucide:copy'" />
          </template>
        </NButton>
      </template>
      {{ hasCopied ? t.chat.copied : t.chat.copy }}
    </NTooltip>

    <!-- Regenerate -->
    <NTooltip v-if="showRegenerate">
      <template #trigger>
        <NButton quaternary size="tiny" @click="emit('regenerate')">
          <template #icon><Icon icon="lucide:refresh-cw" /></template>
        </NButton>
      </template>
      {{ t.chat.retry }}
    </NTooltip>

    <!-- Edit -->
    <NTooltip v-if="showEdit">
      <template #trigger>
        <NButton quaternary size="tiny" @click="emit('edit')">
          <template #icon><Icon icon="lucide:pencil" /></template>
        </NButton>
      </template>
      {{ t.common.edit }}
    </NTooltip>

    <!-- Extra actions slot -->
    <slot name="extra" />
  </div>
</template>
