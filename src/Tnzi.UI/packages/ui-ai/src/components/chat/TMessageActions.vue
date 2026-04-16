<script setup lang="ts">
/**
 * @experimental
 * TMessageActions — Action button row shown below an assistant message.
 *
 * Renders a horizontal row of pill-shaped buttons matching Manus's
 * post-message action cluster (copy / Start agent / Create dropdown).
 * The first button is rendered as an icon-only square (typically copy);
 * subsequent buttons are labeled pills.
 *
 * Three slots so consumers can wire any combination of buttons:
 *
 *   - `#default` — the entire button row (overrides the built-in
 *     copy + actions list)
 *   - `#actions` — additional buttons after copy and before any custom
 *     content; receives `(copy)` slot prop
 *
 * For the simple case use the `actions` prop with `[{ icon, label }]`
 * tuples; the component renders them automatically.
 */
import { ref } from 'vue'
import { Icon } from '@iconify/vue'

export interface MessageAction {
  /** Lucide icon id. */
  icon: string
  /** Display label. */
  label: string
  /** Optional `--primary` style variant for emphasis. */
  primary?: boolean
  /** Optional click handler. */
  onClick?: () => void
}

const props = withDefaults(
  defineProps<{
    /** Text to copy when the copy button is pressed. */
    content: string
    /** Custom action buttons. */
    actions?: readonly MessageAction[]
    /** Hide the built-in copy button. */
    hideCopy?: boolean
  }>(),
  {
    actions: () => [],
    hideCopy: false,
  },
)

const emit = defineEmits<{
  copy: [content: string]
  action: [action: MessageAction, index: number]
}>()

const copied = ref(false)

async function handleCopy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(props.content)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    /* clipboard permission denied — silent */
  }
  emit('copy', props.content)
}

function handleAction(a: MessageAction, i: number): void {
  a.onClick?.()
  emit('action', a, i)
}
</script>

<template>
  <div class="t-message-actions">
    <button
      v-if="!hideCopy"
      type="button"
      class="t-message-actions__btn t-message-actions__btn--icon"
      :aria-label="copied ? 'Copied' : 'Copy'"
      @click="handleCopy"
    >
      <Icon :icon="copied ? 'lucide:check' : 'lucide:copy'" />
    </button>

    <button
      v-for="(a, i) in actions"
      :key="i"
      type="button"
      class="t-message-actions__btn"
      :class="{ 't-message-actions__btn--primary': a.primary }"
      @click="handleAction(a, i)"
    >
      <Icon :icon="a.icon" />
      <span>{{ a.label }}</span>
    </button>

    <slot />
  </div>
</template>

<style scoped>
.t-message-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}
.t-message-actions__btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  background: var(--tnzi-ai-surface, #ffffff);
  color: var(--tnzi-ai-text, #1a1a1a);
  border-radius: 999px;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: background 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-message-actions__btn:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-message-actions__btn .iconify {
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-message-actions__btn--icon {
  width: 30px;
  padding: 0;
  justify-content: center;
}
.t-message-actions__btn--primary .iconify {
  color: var(--tnzi-ai-accent, #3b82f6);
}
</style>
