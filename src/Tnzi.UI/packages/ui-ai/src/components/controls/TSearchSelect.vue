<script setup lang="ts" generic="T extends string | number">
/**
 * TSearchSelect - a dropdown that picks one out of many: the field of `TSelect`, opening a list
 * with a search box above it. For a linked record out of hundreds or thousands.
 *
 * Two sources, one look:
 *   - `options`: every choice is here; typing filters them by label and description (case and
 *     accents ignored);
 *   - `load(query, { offset, signal })`: the choices are fetched a page at a time, for the words
 *     typed (after `debounce` ms), with the next page fetched as the list is scrolled to its end
 *     (or the keyboard walks past its last row). A query typed on aborts the request before it.
 *     The chosen record is usually not on the first page, so give its label in `selectedLabel`.
 *
 * The list shows a loading row while a first page is on its way, "no matches" for an empty
 * answer, and the failure with Retry when a load fails.
 *
 * Keyboard: closed, Down / Up / Enter / Space open it, and typing opens it with what was typed in
 * the search box. Open, focus is in the search box (a combobox: the option in hand is
 * `aria-activedescendant`), Up / Down move through the options (skipping disabled ones), Enter
 * picks, Escape closes the list and goes no further (a dialog around it stays), Tab closes it and
 * moves on. Text being composed by an input method is left alone.
 *
 * `class` and `style` go on the box that lays the field out; every other attribute goes on the
 * field (a `TField`'s slot gives it its `id`). A pick also fires a native `change` on the field.
 */
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, useAttrs, useId, watch } from 'vue'
import { Icon } from '@iconify/vue'
import TPopoverMenu from '../overlay/TPopoverMenu.vue'
import TMenuItem from '../overlay/TMenuItem.vue'
import { afterMenuPlaced } from '../overlay/menu-keys'
import TSpinner from './TSpinner.vue'
import { useControlsText } from './controls-text'
import {
  createSearchSelectSource,
  SEARCH_SELECT_DEBOUNCE_MS,
  type SearchSelectLoader,
  type TSearchSelectOption,
} from './search-select'

const props = withDefaults(
  defineProps<{
    modelValue: T | null
    /** Every choice, filtered as the user types. Not read when `load` is given. */
    options?: readonly TSearchSelectOption<T>[]
    /** Fetches the choices a page at a time for the words typed. */
    load?: SearchSelectLoader<T>
    /** The chosen value's label when the listed options do not hold it (a record not on the first page). */
    selectedLabel?: string
    /** Shown when nothing is chosen. */
    placeholder?: string
    /** The search box's placeholder; the controls' "Search" when left out. */
    searchPlaceholder?: string
    disabled?: boolean
    /** A button in the field that sets the value to `null`. */
    clearable?: boolean
    size?: 'sm' | 'md'
    /** Milliseconds between the last key and the loader's call. */
    debounce?: number
  }>(),
  {
    options: undefined,
    load: undefined,
    selectedLabel: '',
    placeholder: '',
    searchPlaceholder: '',
    disabled: false,
    clearable: false,
    size: 'sm',
    debounce: SEARCH_SELECT_DEBOUNCE_MS,
  },
)
const emit = defineEmits<{
  'update:modelValue': [value: T | null]
  /** The option picked (null when cleared), for its label and description. */
  select: [option: TSearchSelectOption<T> | null]
}>()

defineOptions({ inheritAttrs: false })

/** The menu's width beside a narrow field, and what it keeps clear of the window's edges. */
const MENU_WIDTH = 320
const WINDOW_EDGE = 16
/** How near the end of the list a scroll fetches the next page, in pixels. */
const LOAD_MORE_MARGIN = 48

const attrs = useAttrs()
const text = useControlsText()
const listId = useId()
const open = ref(false)
const active = ref(-1)
const menuWidth = ref(0)
const trigger = ref<HTMLButtonElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const list = ref<HTMLElement | null>(null)
/** The last option picked here, whose label stays known when the list no longer holds it. */
const picked = shallowRef<TSearchSelectOption<T> | null>(null)

const source = createSearchSelectSource<T>({
  options: () => props.options,
  load: () => props.load,
  debounce: () => props.debounce,
})
const { query, items, loading, loadingMore, error, hasMore } = source

const label = computed(() => {
  const value = props.modelValue
  if (value === null || value === undefined) return ''
  const known = items.value.find((option) => option.value === value)
    ?? props.options?.find((option) => option.value === value)
    ?? (picked.value?.value === value ? picked.value : null)
  return known?.label ?? (props.selectedLabel || String(value))
})
const empty = computed(() => props.modelValue === null || props.modelValue === undefined)

/* A function, not a computed: `useAttrs()` is not reactive, so the template reads it on every render. */
function fieldAttrs(): Record<string, unknown> {
  const { class: _class, style: _style, ...rest } = attrs
  return rest
}
const optionId = (index: number): string => `${listId}-${index}`

/** The list scrolls by itself, not its ancestors: the page or dialog behind stays where it is. */
function reveal(): void {
  const box = list.value
  const row = box?.querySelector<HTMLElement>(`#${CSS.escape(optionId(active.value))}`)
  if (!box || !row) return
  if (row.offsetTop < box.scrollTop) box.scrollTop = row.offsetTop
  else if (row.offsetTop + row.offsetHeight > box.scrollTop + box.clientHeight) box.scrollTop = row.offsetTop + row.offsetHeight - box.clientHeight
}

function setActive(index: number): void {
  active.value = index
  void nextTick(reveal)
}

/** The next enabled option from `from` in direction `step`, not wrapping; `from` when there is none. */
function nextEnabled(from: number, step: 1 | -1): number {
  for (let index = from + step; index >= 0 && index < items.value.length; index += step) {
    if (!items.value[index]?.disabled) return index
  }
  return from
}

/** The chosen option when it is listed, else the first enabled one. */
function initialActive(): number {
  const chosen = items.value.findIndex((option) => option.value === props.modelValue && !option.disabled)
  return chosen >= 0 ? chosen : nextEnabled(-1, 1)
}

function show(initialQuery = ''): void {
  if (props.disabled || open.value) return
  const available = typeof window === 'undefined' ? Number.POSITIVE_INFINITY : window.innerWidth - WINDOW_EDGE
  menuWidth.value = Math.min(trigger.value?.offsetWidth ?? 0, available)
  open.value = true
  if (initialQuery) source.search(initialQuery)
  else source.refresh()
  active.value = initialActive()
  void nextTick(() => {
    // TPopoverMenu names its box a menu; here the list inside it is what the search box controls.
    list.value?.parentElement?.parentElement?.setAttribute('role', 'none')
    reveal()
  })
  // Not on the next tick: the menu is hidden until placed, and a hidden field takes no focus.
  afterMenuPlaced(() => {
    if (open.value) input.value?.focus()
  })
}

function close(refocus = true): void {
  open.value = false
  if (refocus) trigger.value?.focus()
}

function pick(option: TSearchSelectOption<T> | undefined): void {
  if (!option || option.disabled) return
  picked.value = option
  close()
  emit('select', option)
  if (option.value === props.modelValue) return
  emit('update:modelValue', option.value)
  trigger.value?.dispatchEvent(new Event('change', { bubbles: true }))
}

function clear(): void {
  picked.value = null
  emit('select', null)
  emit('update:modelValue', null)
  trigger.value?.dispatchEvent(new Event('change', { bubbles: true }))
  trigger.value?.focus()
}

const isTyping = (event: KeyboardEvent): boolean => event.key.length === 1 && event.key !== ' ' && !event.ctrlKey && !event.metaKey && !event.altKey
/* An input method composing text: Safari fires `compositionend` before the committing key, which then reads 229. */
const composing = (event: KeyboardEvent): boolean => event.isComposing || event.keyCode === 229

function onTriggerKey(event: KeyboardEvent): void {
  if (composing(event)) return
  if (open.value) {
    // Typed in the moment before the search box took focus: it belongs in the box.
    if (isTyping(event)) {
      event.preventDefault()
      source.search(query.value + event.key)
    }
    return
  }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    show()
  } else if (isTyping(event)) {
    event.preventDefault()
    show(event.key)
  }
}

function onInputKey(event: KeyboardEvent): void {
  if (composing(event)) return
  if (event.key === 'Tab') {
    close(false)
    return
  }
  if (event.key === 'Escape') close()
  else if (event.key === 'ArrowDown') {
    const next = nextEnabled(active.value, 1)
    if (next === active.value || next >= items.value.length - 1) source.loadMore()
    setActive(next)
  } else if (event.key === 'ArrowUp') setActive(nextEnabled(active.value, -1))
  else if (event.key === 'Enter') pick(items.value[active.value])
  else return
  event.preventDefault()
  event.stopPropagation()
}

function onInput(event: Event): void {
  source.search((event.target as HTMLInputElement).value)
}

function onScroll(): void {
  const box = list.value
  if (box && box.scrollTop + box.clientHeight >= box.scrollHeight - LOAD_MORE_MARGIN) source.loadMore()
}

// A new answer: the option in hand is the chosen one again, or the first.
watch(items, (next, previous) => {
  if (!open.value) return
  const appended = previous.length > 0 && next.length > previous.length && next[0] === previous[0]
  if (!appended) setActive(initialActive())
})
watch(open, (isOpen) => {
  if (!isOpen) source.reset()
})

onBeforeUnmount(() => source.reset())
</script>

<template>
  <div class="t-search-select" :class="attrs.class" :style="(attrs.style as string | undefined)">
    <button
      v-bind="fieldAttrs()"
      ref="trigger"
      type="button"
      class="t-select t-search-select__trigger"
      :class="{ 't-select--md': size === 'md', 'is-placeholder': empty, 'has-clear': clearable && !empty && !disabled }"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :disabled="disabled"
      :data-value="modelValue ?? ''"
      @click="open ? close() : show()"
      @keydown="onTriggerKey"
      @keyup.space.prevent
    >
      <span class="t-search-select__label">{{ label || placeholder }}</span>
    </button>
    <button
      v-if="clearable && !empty && !disabled"
      type="button"
      class="t-search-select__clear"
      :aria-label="text.clear"
      :title="text.clear"
      @click="clear"
    >
      <Icon icon="lucide:x" aria-hidden="true" />
    </button>
    <TPopoverMenu
      v-model="open"
      align="left"
      placement="bottom"
      class="t-search-select__menu"
      :min-width="menuWidth"
      :max-width="Math.max(menuWidth, MENU_WIDTH)"
    >
      <div class="t-search-select__panel">
        <input
          ref="input"
          class="t-input t-search-select__input"
          type="text"
          enterkeyhint="search"
          role="combobox"
          autocomplete="off"
          spellcheck="false"
          aria-autocomplete="list"
          aria-expanded="true"
          :aria-controls="listId"
          :aria-activedescendant="!loading && active >= 0 && items[active] ? optionId(active) : undefined"
          :aria-busy="loading || loadingMore"
          :placeholder="searchPlaceholder || text.search"
          :value="query"
          @input="onInput"
          @keydown="onInputKey"
        >
        <div :id="listId" ref="list" class="t-search-select__list t-ai-scroll" role="listbox" @scroll="onScroll" @mousedown.prevent>
          <template v-if="!loading">
            <TMenuItem
              v-for="(option, index) in items"
              :id="optionId(index)"
              :key="String(option.value)"
              role="option"
              tabindex="-1"
              class="t-search-select__option"
              :class="{ 'is-active': index === active }"
              :aria-selected="option.value === modelValue"
              :aria-disabled="option.disabled || undefined"
              :title="option.description ? `${option.label} (${option.description})` : option.label"
              :data-value="option.value"
              :value="option.description ?? ''"
              :checked="option.value === modelValue"
              :disabled="option.disabled"
              @pointermove="!option.disabled && (active = index)"
              @click="pick(option)"
            >
              {{ option.label }}
            </TMenuItem>
          </template>
          <div v-if="loading || loadingMore" class="t-search-select__status" role="status">
            <TSpinner size="sm" />
            <span>{{ text.loading }}</span>
          </div>
          <div v-else-if="error" class="t-search-select__status is-error" role="alert">
            <span>{{ text.loadFailed }}</span>
            <button type="button" class="t-search-select__retry" @click="source.retry()">{{ text.retry }}</button>
          </div>
          <div v-else-if="items.length === 0" class="t-search-select__status" role="status">{{ text.noMatches }}</div>
          <div v-else-if="hasMore" class="t-search-select__more" aria-hidden="true" />
        </div>
      </div>
    </TPopoverMenu>
  </div>
</template>

<style scoped>
/* No weight of its own: a page's layout class on the field sets its width. */
:where(.t-search-select) {
  position: relative;
  display: block;
  width: 100%;
  min-width: 0;
}
.t-search-select__trigger {
  display: block;
  text-align: left;
}
.t-search-select__trigger.has-clear {
  padding-right: 52px;
}
.t-search-select__trigger.is-placeholder {
  color: var(--tnzi-ai-text-tertiary);
}
.t-search-select__label {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-search-select__clear {
  position: absolute;
  top: 50%;
  right: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border: 0;
  border-radius: 999px;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  cursor: pointer;
  transform: translateY(-50%);
}
.t-search-select__clear:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-search-select__clear:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
.t-search-select__panel {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.t-search-select__input {
  width: 100%;
}
.t-search-select__list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: min(280px, calc(100vh - 80px));
  overflow-y: auto;
  overscroll-behavior: contain;
}
.t-search-select__option {
  flex: none;
}
.t-search-select__option.is-active {
  background: var(--tnzi-ai-hover);
}
.t-search-select__status {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 36px;
  padding: 6px 8px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
}
.t-search-select__status.is-error {
  color: var(--tnzi-ai-danger);
}
.t-search-select__retry {
  padding: 2px 8px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font: inherit;
  cursor: pointer;
}
.t-search-select__retry:hover {
  background: var(--tnzi-ai-hover);
}
/* Room past the last row, so the scroll that fetches the next page can happen. */
.t-search-select__more {
  flex: none;
  height: 8px;
}
@media (pointer: coarse) {
  .t-search-select__option {
    height: 40px;
  }
}
</style>
