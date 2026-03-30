<script setup lang="ts">
/**
 * TMessageFeedback — Thumbs up/down feedback
 *
 * Allows users to rate assistant messages as positive or negative.
 * Thumbs-down shows an inline reason input on first click.
 */

import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/primitives/ui/tooltip';

export type FeedbackValue = 'positive' | 'negative' | null;

const props = defineProps<{
  value: FeedbackValue;
}>();

const emit = defineEmits<{
  feedback: [type: FeedbackValue, reason?: string];
}>();

const t = useAiI18n();
const showReasonInput = ref(false);
const reason = ref('');

const isPositive = computed(() => props.value === 'positive');
const isNegative = computed(() => props.value === 'negative');

function handlePositive(): void {
  showReasonInput.value = false;
  emit('feedback', isPositive.value ? null : 'positive');
}

function handleNegative(): void {
  if (isNegative.value) {
    // Toggle off
    showReasonInput.value = false;
    emit('feedback', null);
  } else {
    // Show reason input
    showReasonInput.value = true;
  }
}

function submitReason(): void {
  emit('feedback', 'negative', reason.value || undefined);
  showReasonInput.value = false;
  reason.value = '';
}
</script>

<template>
  <TooltipProvider>
    <div class="flex items-center gap-1">
      <!-- Thumbs up -->
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            class="size-7"
            :class="isPositive ? 'text-ai-node-completed' : ''"
            @click="handlePositive"
          >
            <Icon icon="lucide:thumbs-up" class="size-3.5" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.feedback.helpful }}</TooltipContent>
      </Tooltip>

      <!-- Thumbs down -->
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            class="size-7"
            :class="isNegative ? 'text-ai-node-failed' : ''"
            @click="handleNegative"
          >
            <Icon icon="lucide:thumbs-down" class="size-3.5" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.feedback.notHelpful }}</TooltipContent>
      </Tooltip>

      <!-- Reason input (inline, appears when thumbs-down first clicked) -->
      <div
        v-if="showReasonInput"
        class="ml-1 flex items-center gap-1"
      >
        <input
          v-model="reason"
          type="text"
          class="h-7 w-48 rounded-md border border-border bg-background px-2 text-xs text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring"
          :placeholder="t.feedback.reasonPlaceholder"
          @keydown.enter="submitReason"
        />
        <Button variant="ghost" size="icon-sm" class="size-7" @click="submitReason">
          <Icon icon="lucide:send" class="size-3.5" />
        </Button>
      </div>
    </div>
  </TooltipProvider>
</template>
