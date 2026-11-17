<script setup lang="ts" generic="T extends string | number | null">
/**
 * TSelect - a dropdown that picks one: a field in the `.t-select` look that
 * opens the package's popover menu, the chosen option checked.
 *
 * The menu is drawn on the popover layer, so a dialog's body or a scrolling
 * panel does not clip it, and it opens upward when there is no room below.
 * It is as wide as the field (at least 320px when the field is narrower,
 * inside the window).
 *
 * `class` and `style` go on the box that lays the field out; every other
 * attribute (`id`, `aria-*`, `data-*`, `title`) goes on the field itself, so
 * it fits a `TField`'s slot. The field carries the value in `data-value`.
 *
 * Keyboard, as a native select: focus stays on the field while the list is
 * open, the option in hand being `aria-activedescendant`, so a dialog's focus
 * trap has nothing to follow. Closed, Down / Up / Enter / Space open it and
 * typing opens it at the first match. Open, Up / Down move (skipping disabled
 * options, wrapping), Home / End go to the ends, typing jumps to the next
 * option that starts with what was typed, Enter and Space pick, Escape closes
 * the list and goes no further (a dialog around it stays), Tab closes it and
 * moves on.
 *
 * `bare` draws the field with no box of its own, as wide as its value with
 * the arrow right after it: the "From" row of a letter, beside `TInlineField`
 * and `.t-input--bare` fields (the row shows focus; on its own the field shows
 * the keyboard focus line).
 *
 * A pick also fires a native `change` on the field, so a form that notes edits
 * by listening on a container sees it as it would a native select's.
 */
import { computed, nextTick, onBeforeUnmount, ref, useAttrs, useId } from 'vue'
import TPopoverMenu from '../overlay/TPopoverMenu.vue'
import TMenuItem from '../overlay/TMenuItem.vue'
import type { TSelectOption } from './options'

const props = withDefaults(
  defineProps<{
    modelValue: T
    options: readonly TSelectOption<T>[]
    disabled?: boolean
    /** Shown when the value is none of the options. */
    placeholder?: string
    size?: 'sm' | 'md'
    /** No box of its own: a field written on a line (in a `TInlineField`). */
    bare?: boolean
  }>(),
  { disabled: false, placeholder: '', size: 'sm', bare: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: T] }>()

defineOptions({ inheritAttrs: false })

/** The menu's width beside a narrow field, and what it keeps clear of the window's edges. */
const MENU_WIDTH = 320
const WINDOW_EDGE = 16
/** A pause this long between keys starts a new type-ahead word. */
const TYPING_PAUSE_MS = 500

const attrs = useAttrs()
const listId = useId()
const open = ref(false)
const active = ref(-1)
const menuWidth = ref(0)
const trigger = ref<HTMLButtonElement | null>(null)
const list = ref<HTMLElement | null>(null)
let typed = ''
let typingTimer: ReturnType<typeof setTimeout> | undefined

const selectedIndex = computed(() => props.options.findIndex((option) => option.value === props.modelValue))
const selectedLabel = computed(() => props.options[selectedIndex.value]?.label ?? '')
/* A function, not a computed: `useAttrs()` is not reactive, so the template reads it on every render. */
function fieldAttrs(): Record<string, unknown> {
  const { class: _class, style: _style, ...rest } = attrs
  return rest
}
const optionId = (index: number): string => `${listId}-${index}`

/** The list scrolls by itself, not its ancestors: the page or dialog behind stays where it is. */
function reveal(): void {
  const box = list.value
  const row = box?.children[active.value] as HTMLElement | undefined
  if (!box || !row) return
  if (row.offsetTop < box.scrollTop) box.scrollTop = row.offsetTop
  else if (row.offsetTop + row.offsetHeight > box.scrollTop + box.clientHeight) box.scrollTop = row.offsetTop + row.offsetHeight - box.clientHeight
}

function setActive(index: number): void {
  active.value = index
  void nextTick(reveal)
}

/** The next enabled option from `from` in direction `step`, wrapping; -1 when none is enabled. */
function nextEnabled(from: number, step: 1 | -1): number {
  const count = props.options.length
  const start = from < 0 && step < 0 ? 0 : from
  for (let hop = 1; hop <= count; hop++) {
    const index = (((start + step * hop) % count) + count) % count
    if (!props.options[index]?.disabled) return index
  }
  return -1
}

function show(): void {
  if (props.disabled || open.value) return
  const available = typeof window === 'undefined' ? Number.POSITIVE_INFINITY : window.innerWidth - WINDOW_EDGE
  menuWidth.value = Math.min(trigger.value?.offsetWidth ?? 0, available)
  const chosen = props.options[selectedIndex.value]
  active.value = chosen && !chosen.disabled ? selectedIndex.value : nextEnabled(-1, 1)
  open.value = true
  void nextTick(() => {
    // TPopoverMenu names its box a menu; here the list inside it is what the field controls.
    list.value?.parentElement?.setAttribute('role', 'none')
    reveal()
  })
}

function close(): void {
  open.value = false
  trigger.value?.focus()
}

function pick(option: TSelectOption<T> | undefined): void {
  if (!option || option.disabled) return
  close()
  if (option.value === props.modelValue) return
  emit('update:modelValue', option.value)
  trigger.value?.dispatchEvent(new Event('change', { bubbles: true }))
}

/** Goes to the next option that starts with what was just typed, as a native select does. */
function typeAhead(char: string): void {
  clearTimeout(typingTimer)
  typed += char.toLocaleLowerCase()
  typingTimer = setTimeout(() => (typed = ''), TYPING_PAUSE_MS)
  const count = props.options.length
  const from = typed.length === 1 ? active.value + 1 : Math.max(active.value, 0)
  for (let hop = 0; hop < count; hop++) {
    const index = (from + hop) % count
    const option = props.options[index]
    if (option && !option.disabled && option.label.toLocaleLowerCase().startsWith(typed)) {
      setActive(index)
      return
    }
  }
}

const isTyping = (event: KeyboardEvent): boolean => event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey

function onClosedKey(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    show()
  } else if (isTyping(event)) {
    event.preventDefault()
    show()
    typeAhead(event.key)
  }
}

function onOpenKey(event: KeyboardEvent): void {
  // A space while a word is being typed is part of the word ("New York").
  const isChar = isTyping(event) && (event.key !== ' ' || typed.length > 0)
  if (event.key === 'Tab') {
    open.value = false
    return
  }
  if (event.key === 'Escape') close()
  else if (event.key === 'ArrowDown') setActive(nextEnabled(active.value, 1))
  else if (event.key === 'ArrowUp') setActive(nextEnabled(active.value, -1))
  else if (event.key === 'Home') setActive(nextEnabled(-1, 1))
  else if (event.key === 'End') setActive(nextEnabled(-1, -1))
  else if (isChar) typeAhead(event.key)
  else if (event.key === 'Enter' || event.key === ' ') pick(props.options[active.value])
  else return
  event.preventDefault()
  event.stopPropagation()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.isComposing) return
  if (open.value) onOpenKey(event)
  else onClosedKey(event)
}

onBeforeUnmount(() => clearTimeout(typingTimer))
</script>

<template>
  <div class="t-select-field" :class="attrs.class" :style="(attrs.style as string | undefined)">
    <button
      v-bind="fieldAttrs()"
      ref="trigger"
      type="button"
      role="combobox"
      class="t-select t-select-field__trigger"
      :class="{ 't-select--md': size === 'md', 't-select--bare': bare, 'is-placeholder': selectedIndex < 0 }"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :aria-controls="open ? listId : undefined"
      :aria-activedescendant="open && active >= 0 ? optionId(active) : undefined"
      :disabled="disabled"
      :data-value="modelValue ?? ''"
      @click="open ? close() : show()"
      @keydown="onKeydown"
      @keyup.space.prevent
    >
      <span class="t-select-field__label">{{ selectedLabel || placeholder }}</span>
    </button>
    <TPopoverMenu
      v-model="open"
      align="left"
      placement="bottom"
      class="t-select-field__menu"
      :min-width="menuWidth"
      :max-width="Math.max(menuWidth, MENU_WIDTH)"
    >
      <div :id="listId" ref="list" class="t-select-field__list t-ai-scroll" role="listbox" @mousedown.prevent>
        <TMenuItem
          v-for="(option, index) in options"
          :id="optionId(index)"
          :key="String(option.value)"
          role="option"
          tabindex="-1"
          class="t-select-field__option"
          :class="{ 'is-active': index === active }"
          :aria-selected="option.value === modelValue"
          :aria-disabled="option.disabled || undefined"
          :title="option.label"
          :data-value="option.value ?? ''"
          :checked="option.value === modelValue"
          :disabled="option.disabled"
          @pointermove="!option.disabled && (active = index)"
          @click="pick(option)"
        >
          {{ option.label }}
        </TMenuItem>
      </div>
    </TPopoverMenu>
  </div>
</template>

<style scoped>
/* No weight of its own: a page's layout class on the field sets its width. */
:where(.t-select-field) {
  position: relative;
  display: block;
  width: 100%;
  min-width: 0;
}
.t-select-field__trigger {
  display: block;
  text-align: left;
}
.t-select-field__label {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-select-field__trigger.is-placeholder {
  color: var(--tnzi-ai-text-tertiary);
}
.t-select-field__list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: min(280px, calc(100vh - 32px));
  overflow-y: auto;
  overscroll-behavior: contain;
}
.t-select-field__option {
  flex: none;
}
.t-select-field__option.is-active {
  background: var(--tnzi-ai-hover);
}
/* A finger needs more than the mouse's row. */
@media (pointer: coarse) {
  .t-select-field__option {
    height: 40px;
  }
}
</style>
