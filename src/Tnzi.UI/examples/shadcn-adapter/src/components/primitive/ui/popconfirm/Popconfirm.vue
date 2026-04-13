<script setup lang="ts">
/**
 * Popconfirm — lightweight confirmation popup.
 * Wraps Popover with a confirm/cancel button panel.
 * Supports async close guards and loading state.
 */
import { ref } from 'vue';
import { PopoverArrow } from 'reka-ui';
import { Popover, PopoverContent, PopoverTrigger } from '../popover';

export type PopconfirmType = 'warning' | 'error' | 'info';

const props = withDefaults(defineProps<{
  /** Confirm button text. Set null to hide. */
  positiveText?: string | null;
  /** Cancel button text. Set null to hide. */
  negativeText?: string | null;
  /** Description text (alternative to description slot) */
  content?: string;
  /** Icon type — controls icon shape and color */
  type?: PopconfirmType;
  showIcon?: boolean;
  disabled?: boolean;
  /** Return false to prevent close */
  onPositiveClick?: (e: MouseEvent) => Promise<boolean | void> | boolean | void;
  /** Return false to prevent close */
  onNegativeClick?: (e: MouseEvent) => Promise<boolean | void> | boolean | void;
}>(), {
  positiveText: 'Confirm',
  negativeText: 'Cancel',
  type: 'warning',
  showIcon: true,
  disabled: false,
});

const open = ref(false);
const loading = ref(false);

function handleOpenChange(val: boolean) {
  if (props.disabled && val) return;
  open.value = val;
}

async function handlePositiveClick(e: MouseEvent) {
  if (loading.value) return;
  if (props.onPositiveClick) {
    loading.value = true;
    try {
      const result = await Promise.resolve(props.onPositiveClick(e));
      if (result === false) return;
    } finally {
      loading.value = false;
    }
  }
  open.value = false;
}

async function handleNegativeClick(e: MouseEvent) {
  if (props.onNegativeClick) {
    const result = await Promise.resolve(props.onNegativeClick(e));
    if (result === false) return;
  }
  open.value = false;
}

const iconMap: Record<PopconfirmType, string> = {
  warning: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
  error: 'M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z',
  info: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z',
};

const colorMap: Record<PopconfirmType, string> = {
  warning: 'hsl(var(--warning))',
  error: 'hsl(var(--destructive))',
  info: 'hsl(var(--info))',
};
</script>

<template>
  <Popover :open="open" @update:open="handleOpenChange">
    <PopoverTrigger as-child>
      <slot />
    </PopoverTrigger>
    <PopoverContent class="popconfirm-content" side="top" align="center" :side-offset="8">
      <PopoverArrow class="popconfirm-arrow" />
      <div class="popconfirm-body">
        <span v-if="showIcon" class="popconfirm-icon" :style="{ color: colorMap[type] }">
          <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
            <path :d="iconMap[type]" />
          </svg>
        </span>
        <span class="popconfirm-text">
          <slot name="description">{{ content }}</slot>
        </span>
      </div>
      <div v-if="positiveText !== null || negativeText !== null" class="popconfirm-actions">
        <button
          v-if="negativeText !== null"
          class="popconfirm-btn popconfirm-btn-ghost"
          :disabled="loading"
          @click="handleNegativeClick"
        >
          {{ negativeText }}
        </button>
        <button
          v-if="positiveText !== null"
          class="popconfirm-btn popconfirm-btn-primary"
          :disabled="loading"
          @click="handlePositiveClick"
        >
          <svg v-if="loading" class="popconfirm-spinner" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" stroke-linecap="round" opacity="0.25" />
            <path d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
          </svg>
          {{ positiveText }}
        </button>
      </div>
    </PopoverContent>
  </Popover>
</template>

<style scoped>
.popconfirm-content {
  width: auto;
  min-width: 200px;
  max-width: 300px;
}

.popconfirm-arrow {
  fill: hsl(var(--popover));
}

.popconfirm-body {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-bottom: 12px;
}

.popconfirm-icon {
  flex-shrink: 0;
}

.popconfirm-text {
  font-size: 14px;
  line-height: 1.6;
  color: hsl(var(--foreground));
}

.popconfirm-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.popconfirm-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  height: 24px;
  padding: 0 10px;
  font-size: 12px;
  border-radius: var(--radius, 3px);
  cursor: pointer;
  transition: all .2s;
  border: 1px solid transparent;
  white-space: nowrap;
}
.popconfirm-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.popconfirm-btn-ghost {
  background: transparent;
  border-color: hsl(var(--border));
  color: hsl(var(--foreground));
}
.popconfirm-btn-ghost:hover:not(:disabled) {
  background: hsl(var(--accent));
}

.popconfirm-btn-primary {
  background: hsl(var(--primary));
  color: hsl(var(--primary-foreground));
}
.popconfirm-btn-primary:hover:not(:disabled) {
  opacity: 0.85;
}

.popconfirm-spinner {
  width: 12px;
  height: 12px;
  animation: popconfirm-spin 1s linear infinite;
}
@keyframes popconfirm-spin { to { transform: rotate(360deg); } }
</style>
