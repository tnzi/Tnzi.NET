<script setup lang="ts">
/**
 * MessageItem — faithfully ported from naive-ui
 *
 * Animation: CSS classes handle opacity/scale/margin, JS hooks handle maxHeight.
 * overflow: visible (not hidden) so shadow and content are never clipped.
 * The maxHeight animation controls layout space only.
 */
import { ref, onMounted, onUnmounted, watch } from 'vue';
import type { MessageEntry } from './message-store';
import { removeMessage } from './message-store';

const props = defineProps<{
  message: MessageEntry;
}>();

const visible = ref(true);
let timer: ReturnType<typeof setTimeout> | null = null;

function startTimer() {
  if (props.message.duration > 0) {
    timer = setTimeout(() => hide(), props.message.duration);
  }
}
function clearTimer() {
  if (timer) { clearTimeout(timer); timer = null; }
}
function hide() {
  visible.value = false;
  clearTimer();
}

function handleMouseEnter() { if (props.message.keepAliveOnHover) clearTimer(); }
function handleMouseLeave() { if (props.message.keepAliveOnHover) startTimer(); }
function handleClose() { props.message.onClose?.(); hide(); }

// --- JS hooks: only maxHeight (CSS classes handle everything else) ---
// No `done` callback — Vue auto-detects transition end from CSS

function onEnter(el: Element) {
  const htmlEl = el as HTMLElement;
  // Disable transitions, read natural height, set to 0, re-enable, animate to natural
  htmlEl.style.transition = 'none';
  const height = htmlEl.offsetHeight;
  htmlEl.style.maxHeight = '0';
  void htmlEl.offsetWidth;
  htmlEl.style.transition = '';
  htmlEl.style.maxHeight = `${height}px`;
  void htmlEl.offsetWidth;
}

function onAfterEnter(el: Element) {
  (el as HTMLElement).style.maxHeight = '';
}

function onBeforeLeave(el: Element) {
  const htmlEl = el as HTMLElement;
  htmlEl.style.maxHeight = `${htmlEl.offsetHeight}px`;
  void htmlEl.offsetWidth;
}

function onLeave(el: Element) {
  const htmlEl = el as HTMLElement;
  htmlEl.style.maxHeight = '0';
  void htmlEl.offsetWidth;
}

function onAfterLeave() {
  props.message.onAfterLeave?.();
  removeMessage(props.message.key);
}

onMounted(() => { startTimer(); });
onUnmounted(() => { clearTimer(); });
watch(() => props.message.duration, () => { clearTimer(); startTimer(); });

const iconMap: Record<string, string> = {
  info: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z',
  success: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z',
  warning: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
  error: 'M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z',
};
const colorMap: Record<string, string> = {
  info: 'hsl(var(--info))',
  success: 'hsl(var(--success))',
  warning: 'hsl(var(--warning))',
  error: 'hsl(var(--destructive))',
  loading: 'hsl(var(--primary))',
};
</script>

<template>
  <Transition
    name="msg"
    appear
    @enter="onEnter"
    @after-enter="onAfterEnter"
    @before-leave="onBeforeLeave"
    @leave="onLeave"
    @after-leave="onAfterLeave"
  >
    <div
      v-if="visible"
      class="msg-wrapper"
      @mouseenter="handleMouseEnter"
      @mouseleave="handleMouseLeave"
    >
      <div class="msg-body">
        <span v-if="message.showIcon" class="msg-icon">
          <svg
            v-if="message.type === 'loading'"
            class="msg-spinner"
            viewBox="0 0 24 24"
            fill="none"
            :style="{ color: colorMap.loading }"
          >
            <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" stroke-linecap="round" opacity="0.25" />
            <path d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
          </svg>
          <svg v-else viewBox="0 0 24 24" fill="currentColor" :style="{ color: colorMap[message.type] }">
            <path :d="iconMap[message.type]" />
          </svg>
        </span>
        <span class="msg-content">{{ message.content }}</span>
        <button v-if="message.closable" class="msg-close" @click="handleClose">
          <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
            <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
          </svg>
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
/* --- wrapper: layout spacing + transform origin --- */
.msg-wrapper {
  margin: 0 0 8px 0;
  z-index: 0;
  transform-origin: top center;
  display: flex;
}

/* --- transition: hidden state (enter-from / leave-to) --- */
.msg-enter-from,
.msg-leave-to {
  opacity: 0;
  transform: scale(0.85);
  margin-top: 0 !important;
  margin-bottom: 0 !important;
}

/* --- transition: visible state (enter-to / leave-from) --- */
.msg-enter-to,
.msg-leave-from {
  opacity: 1;
  transform: scale(1);
}

/* --- transition: enter timing --- */
.msg-enter-active {
  overflow: visible;
  transition:
    max-height .3s cubic-bezier(.4, 0, .2, 1),
    opacity .3s cubic-bezier(.4, 0, 1, 1),
    margin-top .3s cubic-bezier(.4, 0, .2, 1),
    margin-bottom .3s cubic-bezier(.4, 0, .2, 1),
    transform .3s cubic-bezier(.4, 0, .2, 1);
}

/* --- transition: leave timing --- */
.msg-leave-active {
  overflow: visible;
  transition:
    max-height .3s cubic-bezier(.4, 0, .2, 1),
    opacity .3s cubic-bezier(0, 0, .2, 1),
    margin-top .3s cubic-bezier(.4, 0, .2, 1),
    margin-bottom .3s cubic-bezier(.4, 0, .2, 1),
    transform .3s cubic-bezier(.4, 0, .2, 1);
}

/* --- visual card --- */
.msg-body {
  display: flex;
  align-items: center;
  box-sizing: border-box;
  padding: 10px 20px;
  max-width: 720px;
  min-width: 420px;
  border-radius: var(--radius, 3px);
  background: hsl(var(--popover));
  color: hsl(var(--foreground));
  box-shadow: var(--shadow-2);
  font-size: 14px;
  line-height: 1.6;
}

/* --- icon --- */
.msg-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  margin-right: 10px;
}
.msg-icon svg { width: 20px; height: 20px; }

.msg-spinner { animation: msg-spin 1s linear infinite; }
@keyframes msg-spin { to { transform: rotate(360deg); } }

/* --- content --- */
.msg-content {
  display: inline-block;
  font-size: 14px;
  line-height: 1.6;
}

/* --- close button --- */
.msg-close {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  margin-left: 10px;
  padding: 0;
  border: 0;
  background: transparent;
  color: hsl(var(--muted-foreground));
  cursor: pointer;
  border-radius: var(--radius, 3px);
  transition: color .3s cubic-bezier(.4, 0, .2, 1), background-color .3s cubic-bezier(.4, 0, .2, 1);
}
.msg-close:hover {
  color: hsl(var(--foreground));
  background: rgba(0, 0, 0, 0.09);
}
</style>
