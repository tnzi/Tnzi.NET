<script setup lang="ts">
/**
 * @experimental
 * TCommandPalette - Cmd+K-style action launcher modal.
 *
 * Consumer supplies an action list and v-model:open. Internally uses
 * useCommandPalette for filter/navigation state. Teleported to body.
 */
import { onMounted, onBeforeUnmount, ref, watch, toRef } from 'vue'
import { Icon } from '@iconify/vue'
import { useFocusTrap } from '@tnzi/ui'
import { useCommandPalette, type CommandAction } from '../../headless/useCommandPalette'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    actions: readonly CommandAction[]
    placeholder?: string
    hotkey?: readonly string[] | null
    maxResults?: number
  }>(),
  {
    placeholder: 'Type a command or search…',
    hotkey: () => ['mod', 'k'],
    maxResults: 50,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
}>()

const actionsRef = toRef(() => props.actions as readonly CommandAction[])
const palette = useCommandPalette({ actions: actionsRef, maxResults: props.maxResults })
const inputEl = ref<HTMLInputElement | null>(null)
const dialogEl = ref<HTMLElement | null>(null)

/* Without a trap, Tab walks straight out of an `aria-modal` dialog into the
   page behind it and focus never comes back on close. useFocusTrap keeps Tab
   inside `dialogEl`, moves focus to the search input on open, and restores the
   previously focused element on close. It also owns Escape. */
useFocusTrap(dialogEl, () => palette.open.value, {
  onEscape: () => palette.hide(),
  initialFocus: () => inputEl.value,
})

useBodyScrollLock(() => palette.open.value)

watch(
  () => props.modelValue,
  (next) => {
    if (next) palette.show()
    else palette.hide()
  },
  { immediate: true },
)

watch(palette.open, (val) => {
  if (val !== props.modelValue) emit('update:modelValue', val)
})

function isHotkey(event: KeyboardEvent): boolean {
  if (!props.hotkey || props.hotkey.length === 0) return false
  const needsMod = props.hotkey.includes('mod') || props.hotkey.includes('cmd') || props.hotkey.includes('ctrl')
  const hasMod = event.metaKey || event.ctrlKey
  if (needsMod && !hasMod) return false
  const keyToken = props.hotkey.find((t) => !['mod', 'cmd', 'ctrl', 'shift', 'alt'].includes(t))
  return keyToken ? event.key.toLowerCase() === keyToken.toLowerCase() : false
}

function onKeydown(event: KeyboardEvent): void {
  if (isHotkey(event)) {
    event.preventDefault()
    palette.toggle()
    return
  }
  if (!palette.open.value) return
  // Escape is handled by useFocusTrap's onEscape.
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    palette.moveDown()
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    palette.moveUp()
  } else if (event.key === 'Enter') {
    event.preventDefault()
    void palette.activate()
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
})
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <Teleport to="body">
    <transition name="t-cmdk-fade">
      <div
        v-if="palette.open.value"
        class="t-cmdk-backdrop"
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        @click.self="palette.hide()"
      >
        <div ref="dialogEl" class="t-cmdk">
          <div class="t-cmdk__input-row">
            <Icon icon="lucide:search" class="t-cmdk__icon" />
            <input
              ref="inputEl"
              v-model="palette.query.value"
              type="text"
              class="t-cmdk__input"
              :placeholder="placeholder"
              autocomplete="off"
              spellcheck="false"
            />
            <kbd class="t-cmdk__hint">Esc</kbd>
            <button
              type="button"
              class="t-cmdk__close"
              aria-label="Close"
              @click="palette.hide()"
            >
              <Icon icon="lucide:x" />
            </button>
          </div>

          <div class="t-cmdk__results" role="listbox">
            <div
              v-for="(action, idx) in palette.results.value"
              :key="action.id"
              class="t-cmdk__item"
              :class="{ 't-cmdk__item--active': idx === palette.highlightedIndex.value }"
              role="option"
              :aria-selected="idx === palette.highlightedIndex.value"
              @click="palette.highlightedIndex.value = idx; palette.activate()"
              @mouseenter="palette.highlightedIndex.value = idx"
            >
              <Icon v-if="action.icon" :icon="action.icon" class="t-cmdk__item-icon" />
              <div class="t-cmdk__item-body">
                <div class="t-cmdk__item-label">{{ action.label }}</div>
                <div v-if="action.description" class="t-cmdk__item-desc">{{ action.description }}</div>
              </div>
              <div v-if="action.category" class="t-cmdk__item-category">{{ action.category }}</div>
            </div>
            <div v-if="palette.results.value.length === 0" class="t-cmdk__empty">
              No matching actions.
            </div>
          </div>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped>
.t-cmdk-backdrop {
  position: fixed;
  inset: 0;
  background: var(--tnzi-ai-backdrop, rgba(0, 0, 0, 0.6));
  backdrop-filter: blur(var(--tnzi-ai-backdrop-blur, 4px));
  -webkit-backdrop-filter: blur(var(--tnzi-ai-backdrop-blur, 4px));
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  z-index: 100;
}
.t-cmdk {
  width: min(650px, 92vw);
  background: var(--tnzi-ai-surface, #fff);
  color: var(--tnzi-ai-text, inherit);
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: var(--tnzi-ai-modal-radius, 20px);
  box-shadow: 0 24px 48px rgba(0, 0, 0, 0.08);
  overflow: hidden;
}
.t-cmdk__input-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--tnzi-ai-divider, #ebebeb);
}
.t-cmdk__icon {
  font-size: 18px;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
}
.t-cmdk__input {
  flex: 1;
  border: none;
  outline: none;
  background: transparent;
  font-family: inherit;
  font-size: 16px;
  color: var(--tnzi-ai-text, inherit);
}
.t-cmdk__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
}
.t-cmdk__hint {
  font-size: 10px;
  padding: 2px 6px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  border-radius: 4px;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  font-family: var(--tnzi-ai-font-mono, monospace);
}
.t-cmdk__close {
  width: 28px;
  height: 28px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  background: transparent;
  border-radius: 999px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  transition: background 120ms ease, color 120ms ease;
}
.t-cmdk__close:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #000);
}
.t-cmdk__results {
  max-height: 440px;
  overflow-y: auto;
  padding: 8px;
}
.t-cmdk__item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  cursor: pointer;
}
.t-cmdk__item:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
}
.t-cmdk__item--active,
.t-cmdk__item--active:hover {
  background: var(--tnzi-ai-selected, rgba(0, 0, 0, 0.08));
}
.t-cmdk__item-icon {
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  flex-shrink: 0;
}
.t-cmdk__item-body { flex: 1; min-width: 0; }
.t-cmdk__item-label {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-ai-text, #000);
}
.t-cmdk__item-desc {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  margin-top: 2px;
}
.t-cmdk__item-category {
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  flex-shrink: 0;
}
.t-cmdk__empty {
  padding: 32px;
  text-align: center;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.4));
  font-size: 13px;
}
.t-cmdk-fade-enter-active,
.t-cmdk-fade-leave-active { transition: opacity 120ms ease; }
.t-cmdk-fade-enter-from,
.t-cmdk-fade-leave-to { opacity: 0; }
</style>
