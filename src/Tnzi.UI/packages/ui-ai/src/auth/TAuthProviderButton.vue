<script setup lang="ts">
/**
 * One third-party sign-in button.
 *
 * `radius: 10px` here against `8px` on the input and primary button is not a
 * slip - it is the measured hierarchy of the reference design: the provider
 * stack reads as a group of chips, the field + primary action as the form.
 * Unifying them flattens that distinction.
 */
import { Icon } from '@iconify/vue';
import type { LoginThirdPartyProvider } from '@tnzi/ui';

defineProps<{
  provider: LoginThirdPartyProvider;
  label: string;
  disabled?: boolean;
}>();

const emit = defineEmits<{ (e: 'select'): void }>();
</script>

<template>
  <button
    type="button"
    class="t-auth-provider"
    :disabled="disabled"
    @click="emit('select')"
  >
    <Icon
      v-if="provider.icon"
      class="t-auth-provider__icon"
      :icon="provider.icon"
      :style="{ color: provider.color || undefined }"
    />
    <span class="t-auth-provider__label">{{ label }}</span>
  </button>
</template>

<style scoped>
.t-auth-provider {
  display: flex;
  align-items: center;
  gap: 12px;
  height: 40px;
  /* Right padding leaves room for the label to stay optically centred against
     the icon on the left, matching the reference. */
  padding: 0 54px 0 14px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 14px;
  font-weight: 500;
  font-family: inherit;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
  width: 100%;
  box-sizing: border-box;
}

.t-auth-provider:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
}

.t-auth-provider:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.t-auth-provider__icon {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}

.t-auth-provider__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
