<script setup lang="ts">
/**
 * NotificationItem — naive-ui style
 *
 * Animation: CSS classes handle opacity/translateX, JS hooks handle maxHeight.
 * Slides in from the right (or left for left placements).
 */
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';
import type { NotificationEntry } from './notification-store';
import type { NotificationPlacement } from './types';
import { removeNotification } from './notification-store';

const props = defineProps<{
  notification: NotificationEntry;
  placement: NotificationPlacement;
}>();

const visible = ref(true);
let timer: ReturnType<typeof setTimeout> | null = null;

const isLeftPlacement = computed(() => props.placement.endsWith('-left'));

function startTimer() {
  if (props.notification.duration > 0) {
    timer = setTimeout(() => hide(), props.notification.duration);
  }
}
function clearTimer() {
  if (timer) { clearTimeout(timer); timer = null; }
}
function hide() {
  visible.value = false;
  clearTimer();
}

function handleMouseEnter() { if (props.notification.keepAliveOnHover) clearTimer(); }
function handleMouseLeave() { if (props.notification.keepAliveOnHover) startTimer(); }
function handleClose() { props.notification.onClose?.(); hide(); }

// --- JS hooks: maxHeight animation ---
function onEnter(el: Element) {
  const htmlEl = el as HTMLElement;
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
  props.notification.onAfterLeave?.();
  removeNotification(props.notification.key);
}

onMounted(() => { startTimer(); });
onUnmounted(() => { clearTimer(); });
watch(() => props.notification.duration, () => { clearTimer(); startTimer(); });

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
};
</script>

<template>
  <Transition
    name="notif"
    appear
    @enter="onEnter"
    @after-enter="onAfterEnter"
    @before-leave="onBeforeLeave"
    @leave="onLeave"
    @after-leave="onAfterLeave"
  >
    <div
      v-if="visible"
      class="notif-wrapper"
      :class="{ 'notif-left': isLeftPlacement }"
      @mouseenter="handleMouseEnter"
      @mouseleave="handleMouseLeave"
    >
      <div class="notif-card">
        <button v-if="notification.closable" class="notif-close" @click="handleClose">
          <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
            <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
          </svg>
        </button>
        <div class="notif-body">
          <span v-if="notification.showIcon" class="notif-icon">
            <svg viewBox="0 0 24 24" fill="currentColor" :style="{ color: colorMap[notification.type] }">
              <path :d="iconMap[notification.type]" />
            </svg>
          </span>
          <div class="notif-main">
            <div v-if="notification.title" class="notif-title">{{ notification.title }}</div>
            <div class="notif-content">{{ notification.content }}</div>
            <div v-if="notification.meta" class="notif-meta">{{ notification.meta }}</div>
          </div>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
/* --- wrapper: layout spacing + transform origin --- */
.notif-wrapper {
  margin: 0 0 8px 0;
  z-index: 0;
  transform-origin: top right;
  display: flex;
  justify-content: flex-end;
}
.notif-wrapper.notif-left {
  transform-origin: top left;
  justify-content: flex-start;
}

/* --- transition: hidden state (enter-from / leave-to) — slide from right --- */
.notif-enter-from,
.notif-leave-to {
  opacity: 0;
  transform: translateX(100%);
  margin-top: 0 !important;
  margin-bottom: 0 !important;
}

/* --- transition: left placement override --- */
.notif-left.notif-enter-from,
.notif-left.notif-leave-to {
  transform: translateX(-100%);
}

/* --- transition: visible state (enter-to / leave-from) --- */
.notif-enter-to,
.notif-leave-from {
  opacity: 1;
  transform: translateX(0);
}

/* --- transition: enter timing --- */
.notif-enter-active {
  overflow: visible;
  transition:
    max-height .3s cubic-bezier(.4, 0, .2, 1),
    opacity .3s cubic-bezier(.4, 0, 1, 1),
    margin-top .3s cubic-bezier(.4, 0, .2, 1),
    margin-bottom .3s cubic-bezier(.4, 0, .2, 1),
    transform .3s cubic-bezier(.4, 0, .2, 1);
}

/* --- transition: leave timing --- */
.notif-leave-active {
  overflow: visible;
  transition:
    max-height .3s cubic-bezier(.4, 0, .2, 1),
    opacity .3s cubic-bezier(0, 0, .2, 1),
    margin-top .3s cubic-bezier(.4, 0, .2, 1),
    margin-bottom .3s cubic-bezier(.4, 0, .2, 1),
    transform .3s cubic-bezier(.4, 0, .2, 1);
}

/* --- visual card --- */
.notif-card {
  position: relative;
  box-sizing: border-box;
  width: 365px;
  padding: 16px 20px;
  border-radius: var(--radius, 3px);
  background: hsl(var(--popover));
  color: hsl(var(--foreground));
  box-shadow: var(--shadow-2);
  border: 1px solid hsl(var(--border));
}

/* --- body: icon + main --- */
.notif-body {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

/* --- icon --- */
.notif-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  margin-top: 2px;
}
.notif-icon svg { width: 28px; height: 28px; }

/* --- main content area --- */
.notif-main {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

/* --- title --- */
.notif-title {
  font-size: 16px;
  font-weight: 600;
  line-height: 1.5;
  color: hsl(var(--foreground));
}

/* --- content --- */
.notif-content {
  font-size: 14px;
  line-height: 1.6;
  color: hsl(var(--muted-foreground));
  margin-top: 4px;
}
.notif-title + .notif-content {
  margin-top: 4px;
}
.notif-content:first-child {
  margin-top: 0;
}

/* --- meta --- */
.notif-meta {
  font-size: 12px;
  line-height: 1.5;
  color: hsl(var(--muted-foreground));
  margin-top: 8px;
  opacity: 0.7;
}

/* --- close button --- */
.notif-close {
  position: absolute;
  top: 12px;
  right: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  padding: 0;
  border: 0;
  background: transparent;
  color: hsl(var(--muted-foreground));
  cursor: pointer;
  border-radius: var(--radius, 3px);
  transition: color .3s cubic-bezier(.4, 0, .2, 1), background-color .3s cubic-bezier(.4, 0, .2, 1);
}
.notif-close:hover {
  color: hsl(var(--foreground));
  background: rgba(0, 0, 0, 0.09);
}
</style>
