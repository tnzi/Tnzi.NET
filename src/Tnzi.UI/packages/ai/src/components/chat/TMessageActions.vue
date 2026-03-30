<script setup lang="ts">
/**
 * TMessageActions — Message action bar
 *
 * Shows copy, regenerate, and edit actions below assistant messages.
 * Appears on hover (opacity-0 → group-hover:opacity-100).
 * Copy shows a check icon for 2 seconds after clicking.
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/primitives/ui/tooltip';

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
  <TooltipProvider>
    <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
      <!-- Copy -->
      <Tooltip v-if="showCopy">
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" class="size-7" @click="handleCopy">
            <Icon
              :icon="hasCopied ? 'lucide:check' : 'lucide:copy'"
              class="size-3.5"
            />
            <span class="sr-only">{{ hasCopied ? t.chat.copied : t.chat.copy }}</span>
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ hasCopied ? t.chat.copied : t.chat.copy }}</TooltipContent>
      </Tooltip>

      <!-- Regenerate -->
      <Tooltip v-if="showRegenerate">
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" class="size-7" @click="emit('regenerate')">
            <Icon icon="lucide:refresh-cw" class="size-3.5" />
            <span class="sr-only">{{ t.chat.retry }}</span>
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.chat.retry }}</TooltipContent>
      </Tooltip>

      <!-- Edit -->
      <Tooltip v-if="showEdit">
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" class="size-7" @click="emit('edit')">
            <Icon icon="lucide:pencil" class="size-3.5" />
            <span class="sr-only">{{ t.common.edit }}</span>
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.common.edit }}</TooltipContent>
      </Tooltip>

      <!-- Extra actions slot -->
      <slot name="extra" />
    </div>
  </TooltipProvider>
</template>
