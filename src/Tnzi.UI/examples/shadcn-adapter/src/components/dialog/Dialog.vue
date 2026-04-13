<script setup lang="ts">
/**
 * Dialog — naive-ui style dialog card with mask + animations.
 *
 * Animation system:
 * - Mask: fade 250ms
 * - Content: scale(0.5→1) + opacity 250ms
 *
 * Matches naive-ui NDialog visual structure and animation exactly.
 */
import { ref, onMounted, onUnmounted, computed } from 'vue';
import type { DialogType } from '../../composables/dialog-state';

const props = withDefaults(defineProps<{
  show: boolean;
  type?: DialogType;
  title?: string;
  content?: string;
  positiveText?: string;
  negativeText?: string;
  closable?: boolean;
  showIcon?: boolean;
  loading?: boolean;
  prompt?: boolean;
  promptPlaceholder?: string;
  inputValue?: string;
}>(), {
  type: 'default',
  closable: true,
  showIcon: true,
  loading: false,
  prompt: false,
});

const emit = defineEmits<{
  positiveClick: [e: MouseEvent];
  negativeClick: [e: MouseEvent];
  inputChange: [value: string];
  close: [];
  maskClick: [];
  esc: [];
  afterLeave: [];
}>();

const localInput = ref(props.inputValue ?? '');
function handleInput(e: Event) {
  localInput.value = (e.target as HTMLInputElement).value;
  emit('inputChange', localInput.value);
}

// --- Focus trap ---
const dialogRef = ref<HTMLElement>();

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    emit('esc');
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleKeydown);
});
onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown);
});

// --- Icon ---
const iconColor = computed(() => {
  const map: Record<string, string> = {
    default: 'hsl(var(--primary))',
    info: 'hsl(var(--info))',
    success: 'hsl(var(--success))',
    warning: 'hsl(var(--warning))',
    error: 'hsl(var(--destructive))',
  };
  return map[props.type] ?? map.default;
});

const iconPath = computed(() => {
  const map: Record<string, string> = {
    default: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z',
    info: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z',
    success: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z',
    warning: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
    error: 'M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z',
  };
  return map[props.type] ?? map.default;
});

// --- Positive button variant ---
const positiveButtonClass = computed(() => {
  const map: Record<string, string> = {
    default: 'dlg-btn-primary',
    info: 'dlg-btn-primary',
    success: 'dlg-btn-success',
    warning: 'dlg-btn-warning',
    error: 'dlg-btn-error',
  };
  return map[props.type] ?? 'dlg-btn-primary';
});

function handleContentAfterLeave() {
  emit('afterLeave');
}
</script>

<template>
  <!-- Fixed container covering viewport -->
  <div class="dlg-container">
    <!-- Mask with fade transition -->
    <Transition name="dlg-mask" appear>
      <div
        v-if="show"
        class="dlg-mask"
        @click="emit('maskClick')"
      />
    </Transition>

    <!-- Scroll wrapper for vertical centering -->
    <div class="dlg-scroll-content">
      <!-- Dialog card with scale-up transition -->
      <Transition name="dlg-content" appear @after-leave="handleContentAfterLeave">
        <div
          v-if="show"
          ref="dialogRef"
          role="dialog"
          aria-modal="true"
          class="dlg-card"
        >
          <!-- Close button -->
          <button
            v-if="closable"
            class="dlg-close"
            @click="emit('close')"
          >
            <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
              <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
            </svg>
          </button>

          <!-- Title row with icon -->
          <div class="dlg-title">
            <span v-if="showIcon" class="dlg-icon" :style="{ color: iconColor }">
              <svg viewBox="0 0 24 24" fill="currentColor" width="28" height="28">
                <path :d="iconPath" />
              </svg>
            </span>
            <span>{{ title }}</span>
          </div>

          <!-- Content -->
          <div v-if="content" class="dlg-content">
            {{ content }}
          </div>
          <slot />

          <!-- Prompt input -->
          <input
            v-if="prompt"
            class="dlg-input"
            :value="localInput"
            :placeholder="promptPlaceholder"
            @input="handleInput"
            @keydown.enter="emit('positiveClick', $event as unknown as MouseEvent)"
          />

          <!-- Action buttons -->
          <div
            v-if="positiveText || negativeText"
            class="dlg-actions"
          >
            <button
              v-if="negativeText"
              class="dlg-btn dlg-btn-ghost"
              @click="emit('negativeClick', $event)"
            >
              {{ negativeText }}
            </button>
            <button
              v-if="positiveText"
              class="dlg-btn"
              :class="positiveButtonClass"
              :disabled="loading"
              @click="emit('positiveClick', $event)"
            >
              <svg
                v-if="loading"
                class="dlg-btn-spinner"
                viewBox="0 0 24 24"
                fill="none"
              >
                <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" stroke-linecap="round" opacity="0.25" />
                <path d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
              </svg>
              {{ positiveText }}
            </button>
          </div>
        </div>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
/* === Container === */
.dlg-container {
  position: fixed;
  left: 0;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 6000;
}

/* === Mask === */
.dlg-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
}

/* Mask animation: 250ms fade */
.dlg-mask-enter-active,
.dlg-mask-leave-active {
  transition: opacity .25s cubic-bezier(0, 0, .2, 1);
}
.dlg-mask-enter-from,
.dlg-mask-leave-to {
  opacity: 0;
}

/* === Scroll content (vertical centering) === */
.dlg-scroll-content {
  position: fixed;
  inset: 0;
  overflow: auto;
  display: flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
}

/* === Dialog card === */
.dlg-card {
  position: relative;
  box-sizing: border-box;
  word-break: break-word;
  line-height: 1.6;
  width: 446px;
  max-width: calc(100vw - 32px);
  margin: auto;
  padding: 16px 28px 20px 28px;
  border-radius: var(--radius, 3px);
  background: hsl(var(--popover));
  color: hsl(var(--foreground));
  box-shadow: 0 3px 6px -4px rgba(0,0,0,.12), 0 6px 16px 0 rgba(0,0,0,.08), 0 9px 28px 8px rgba(0,0,0,.05);
  pointer-events: auto;
}

/* Content animation: 250ms scale(0.5→1) + fade */
.dlg-content-enter-active {
  transform-origin: center center;
  transition: opacity .25s cubic-bezier(0, 0, .2, 1), transform .25s cubic-bezier(0, 0, .2, 1);
}
.dlg-content-leave-active {
  transform-origin: center center;
  transition: opacity .25s cubic-bezier(.4, 0, 1, 1), transform .25s cubic-bezier(.4, 0, 1, 1);
}
.dlg-content-enter-from,
.dlg-content-leave-to {
  opacity: 0;
  transform: scale(0.5);
}

/* === Close button === */
.dlg-close {
  position: absolute;
  top: 16px;
  right: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 0;
  background: transparent;
  color: hsl(var(--muted-foreground));
  cursor: pointer;
  border-radius: var(--radius, 3px);
  transition: color .3s, background-color .3s;
}
.dlg-close:hover {
  color: hsl(var(--foreground));
  background: rgba(0, 0, 0, 0.06);
}

/* === Title === */
.dlg-title {
  display: flex;
  align-items: center;
  font-size: 18px;
  font-weight: 500;
  line-height: 1.6;
  color: hsl(var(--foreground));
}
.dlg-icon {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  margin-right: 8px;
}

/* === Content === */
.dlg-content {
  margin: 8px 0 16px 0;
  font-size: 14px;
  line-height: 1.6;
  color: hsl(var(--muted-foreground));
}

/* === Prompt input === */
.dlg-input {
  display: block;
  width: 100%;
  box-sizing: border-box;
  height: 34px;
  padding: 0 10px;
  margin-bottom: 16px;
  font-size: 14px;
  line-height: 1.6;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius, 3px);
  background: hsl(var(--background));
  color: hsl(var(--foreground));
  outline: none;
  transition: border-color .3s;
}
.dlg-input:focus {
  border-color: hsl(var(--primary));
}

/* === Actions === */
.dlg-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

/* === Buttons === */
.dlg-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  height: 28px;
  padding: 0 12px;
  font-size: 14px;
  line-height: 1;
  border: 1px solid transparent;
  border-radius: var(--radius, 3px);
  cursor: pointer;
  transition: color .3s, background-color .3s, border-color .3s, opacity .3s;
  white-space: nowrap;
}
.dlg-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Ghost button (negative) */
.dlg-btn-ghost {
  background: transparent;
  border-color: hsl(var(--border));
  color: hsl(var(--foreground));
}
.dlg-btn-ghost:hover:not(:disabled) {
  background: hsl(var(--accent));
}

/* Typed positive buttons */
.dlg-btn-primary {
  background: hsl(var(--primary));
  color: hsl(var(--primary-foreground));
}
.dlg-btn-primary:hover:not(:disabled) { opacity: 0.85; }

.dlg-btn-success {
  background: hsl(var(--success));
  color: #fff;
}
.dlg-btn-success:hover:not(:disabled) { opacity: 0.85; }

.dlg-btn-warning {
  background: hsl(var(--warning));
  color: #fff;
}
.dlg-btn-warning:hover:not(:disabled) { opacity: 0.85; }

.dlg-btn-error {
  background: hsl(var(--destructive));
  color: hsl(var(--destructive-foreground));
}
.dlg-btn-error:hover:not(:disabled) { opacity: 0.85; }

/* Button loading spinner */
.dlg-btn-spinner {
  width: 14px;
  height: 14px;
  animation: dlg-spin 1s linear infinite;
}
@keyframes dlg-spin { to { transform: rotate(360deg); } }
</style>
