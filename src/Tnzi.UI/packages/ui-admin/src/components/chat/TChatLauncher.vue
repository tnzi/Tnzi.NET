<template>
  <NBadge :value="unreadCount" :show="unreadCount > 0" :max="99" type="error" :offset="[-2, 2]">
    <button
      ref="btnRef"
      class="t-chat-launcher"
      type="button"
      :title="t('launcher.title')"
      @click="emit('open')"
      @animationend="onAnimEnd"
    >
      <Icon icon="nimbus:chat-dots" :width="20" :height="20" />
    </button>
  </NBadge>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { NBadge } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { ChatNewMessageEffect } from '@tnzi/core/services/chat'
import { translatePageKey } from '../../i18n/translate'

const props = withDefaults(
  defineProps<{
    unreadCount: number
    /** Attention animation style; None disables it. */
    effect?: ChatNewMessageEffect
    /** Bump this counter to (re)play the animation once - e.g. on each new message. */
    attention?: number
  }>(),
  { effect: ChatNewMessageEffect.None, attention: 0 },
)
const emit = defineEmits<{ open: [] }>()
const t = (k: string) => translatePageKey('chat', k)

const btnRef = ref<HTMLButtonElement | null>(null)
const EFFECT_CLASSES = [
  't-chat-launcher--shake',
  't-chat-launcher--pulse',
  't-chat-launcher--blink',
  't-chat-launcher--bounce',
]

function clearAnim(el: HTMLElement): void {
  el.classList.remove(...EFFECT_CLASSES)
}
function onAnimEnd(): void {
  if (btnRef.value) clearAnim(btnRef.value)
}

// Replay the CSS animation each time `attention` bumps, even mid-animation:
// remove the class, force a reflow, then re-add so the keyframes restart.
watch(
  () => props.attention,
  () => {
    const el = btnRef.value
    if (!el || !props.effect || props.effect === ChatNewMessageEffect.None) return
    const cls = `t-chat-launcher--${props.effect.toLowerCase()}`
    clearAnim(el)
    void el.offsetWidth // force reflow
    el.classList.add(cls)
  },
)
</script>

<style scoped>
.t-chat-launcher {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-base-text-muted, #666);
  cursor: pointer;
  transition: background 0.15s;
}
.t-chat-launcher:hover {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.06);
}

/* ── Attention animations (played once per new message when window closed) ── */
@media (prefers-reduced-motion: no-preference) {
  .t-chat-launcher--shake {
    animation: t-chat-shake 0.6s cubic-bezier(0.36, 0.07, 0.19, 0.97) both;
    transform-origin: 50% 0;
  }
  .t-chat-launcher--pulse {
    animation: t-chat-pulse 0.6s ease-out both;
  }
  .t-chat-launcher--blink {
    animation: t-chat-blink 0.9s ease-in-out both;
  }
  .t-chat-launcher--bounce {
    animation: t-chat-bounce 0.7s cubic-bezier(0.28, 0.84, 0.42, 1) both;
  }
}

@keyframes t-chat-shake {
  10%, 90% { transform: rotate(-8deg); }
  20%, 80% { transform: rotate(10deg); }
  30%, 50%, 70% { transform: rotate(-12deg); }
  40%, 60% { transform: rotate(12deg); }
  0%, 100% { transform: rotate(0); }
}
@keyframes t-chat-pulse {
  0% { transform: scale(1); box-shadow: 0 0 0 0 rgb(var(--tnzi-error-rgb, 224 64 64) / 0.5); }
  50% { transform: scale(1.25); }
  100% { transform: scale(1); box-shadow: 0 0 0 12px rgb(var(--tnzi-error-rgb, 224 64 64) / 0); }
}
@keyframes t-chat-blink {
  0%, 100% { opacity: 1; color: var(--tnzi-base-text-muted, #666); }
  25%, 75% { opacity: 0.3; }
  50% { opacity: 1; color: var(--tnzi-primary-600, #158278); }
}
@keyframes t-chat-bounce {
  0%, 100% { transform: translateY(0); }
  30% { transform: translateY(-8px); }
  50% { transform: translateY(0); }
  65% { transform: translateY(-4px); }
  80% { transform: translateY(0); }
}
</style>
