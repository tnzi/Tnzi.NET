<script setup lang="ts">
/**
 * MessageActions — Message action bar
 *
 * Shows copy, regenerate, and edit actions below assistant messages.
 * Appears on hover (opacity-0 → group-hover:opacity-100).
 * Copy shows a check icon for 2 seconds after clicking.
 */

import { Button, Tooltip } from '../../primitives';
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
    <Tooltip v-if="showCopy">
      <template #trigger>
        <Button variant="ghost" size="icon-sm" class="size-7" @click="handleCopy">
          <Icon
            :icon="hasCopied ? 'lucide:check' : 'lucide:copy'"
            class="size-3.5"
          />
          <span class="sr-only">{{ hasCopied ? t.chat.copied : t.chat.copy }}</span>
        </Button>
      </template>
      {{ hasCopied ? t.chat.copied : t.chat.copy }}
    </Tooltip>

    <!-- Regenerate -->
    <Tooltip v-if="showRegenerate">
      <template #trigger>
        <Button variant="ghost" size="icon-sm" class="size-7" @click="emit('regenerate')">
          <Icon icon="lucide:refresh-cw" class="size-3.5" />
          <span class="sr-only">{{ t.chat.retry }}</span>
        </Button>
      </template>
      {{ t.chat.retry }}
    </Tooltip>

    <!-- Edit -->
    <Tooltip v-if="showEdit">
      <template #trigger>
        <Button variant="ghost" size="icon-sm" class="size-7" @click="emit('edit')">
          <Icon icon="lucide:pencil" class="size-3.5" />
          <span class="sr-only">{{ t.common.edit }}</span>
        </Button>
      </template>
      {{ t.common.edit }}
    </Tooltip>

    <!-- Extra actions slot -->
    <slot name="extra" />
  </div>
</template>
