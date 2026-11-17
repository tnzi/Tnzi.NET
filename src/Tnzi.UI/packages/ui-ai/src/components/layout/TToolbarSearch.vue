<script setup lang="ts">
/**
 * @experimental
 * TToolbarSearch - a search that waits as an icon button and opens in place
 * into a compact field.
 *
 * - The icon opens it and moves focus into the field.
 * - Escape clears the query, closes it and gives focus back to the icon.
 * - Leaving the field with nothing typed closes it; with a query it stays
 *   open, so what filters the page stays in view.
 * - It opens already when it starts with a query.
 * - With a query, a clear button (the package's, not the browser's native
 *   search "x", which Chrome draws in its own blue) empties it and keeps the
 *   field open with focus in it.
 *
 * `v-model` is the query as typed. Debouncing it (or not) is the consumer's
 * choice. `v-model:expanded` is optional, for a toolbar that gives an open
 * search the whole row on a phone (`TPageToolbar` does).
 *
 * `inputAttrs` / `buttonAttrs` go on the field and on the icon button (a page's
 * own `data-*` hooks); attributes on the component itself go on its root.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { isImeComposing } from '@tnzi/core/utils'
import { useAiI18n } from '../../i18n/index'
import type { ElementAttrs } from './page-toolbar'

const props = withDefaults(
  defineProps<{
    modelValue: string
    /** Accessible name of the field and of the icon. Defaults to the locale's "Search". */
    label?: string
    /** Placeholder of the field. Defaults to the label. */
    placeholder?: string
    /** Whether it is open. Optional `v-model:expanded`. */
    expanded?: boolean
    /** Width of the open field on a wide screen (number = px). */
    width?: number | string
    /** Longest query the field takes (`maxlength`). */
    maxlength?: number
    /** Attributes for the field (`data-*`, `name`, `enterkeyhint`...). */
    inputAttrs?: ElementAttrs
    /** Attributes for the icon button that opens it. */
    buttonAttrs?: ElementAttrs
  }>(),
  {
    label: undefined,
    placeholder: undefined,
    expanded: undefined,
    width: 200,
    maxlength: undefined,
    inputAttrs: undefined,
    buttonAttrs: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [query: string]
  'update:expanded': [expanded: boolean]
}>()

const t = useAiI18n()
const labelText = computed(() => props.label ?? t.value.toolbar.search)
const placeholderText = computed(() => props.placeholder ?? labelText.value)
const fieldWidth = computed(() => (typeof props.width === 'number' ? `${props.width}px` : props.width))

const localOpen = ref(props.modelValue !== '')
const isOpen = computed(() => props.expanded ?? (localOpen.value || props.modelValue !== ''))

function setOpen(value: boolean): void {
  localOpen.value = value
  if (props.expanded !== value) emit('update:expanded', value)
}

/* A query set from outside (a link, a restored view) opens it. */
watch(
  () => props.modelValue,
  (query) => {
    if (query !== '' && !isOpen.value) setOpen(true)
  },
)

const input = ref<HTMLInputElement | null>(null)
const button = ref<HTMLButtonElement | null>(null)

function open(): void {
  setOpen(true)
  void nextTick(() => input.value?.focus())
}

function clearAndClose(): void {
  if (props.modelValue !== '') emit('update:modelValue', '')
  setOpen(false)
  void nextTick(() => button.value?.focus())
}

/* The clear button keeps the field open: clearing is "search for something
   else", not "stop searching" (that is Escape). */
function clear(): void {
  emit('update:modelValue', '')
  void nextTick(() => input.value?.focus())
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || isImeComposing(event)) return
  /* Handled here: a dialog or menu the toolbar sits in must not take this
     Escape as well (the overlay stack skips a prevented Escape). */
  event.preventDefault()
  event.stopPropagation()
  clearAndClose()
}

/* Leaving for the clear button is not leaving the search. */
function onBlur(event: FocusEvent): void {
  const next = event.relatedTarget as Node | null
  if (next && input.value?.parentElement?.contains(next)) return
  if (props.modelValue.trim() === '') setOpen(false)
}

defineExpose({ open, clear, focus: () => (isOpen.value ? input.value?.focus() : button.value?.focus()) })
</script>

<template>
  <div class="t-toolbar-search" :class="{ 'is-open': isOpen }">
    <div v-if="isOpen" class="t-toolbar-search__field" role="search" :style="{ '--t-toolbar-search-width': fieldWidth }">
      <Icon icon="lucide:search" class="t-toolbar-search__icon" aria-hidden="true" />
      <input
        v-bind="inputAttrs"
        ref="input"
        type="search"
        class="t-toolbar-search__input"
        :class="{ 'has-value': modelValue !== '' }"
        :value="modelValue"
        :placeholder="placeholderText"
        :aria-label="labelText"
        :maxlength="maxlength"
        autocomplete="off"
        @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
        @keydown="onKeydown"
        @blur="onBlur"
      />
      <button
        v-if="modelValue !== ''"
        type="button"
        class="t-toolbar-search__clear"
        :aria-label="t.toolbar.clearSearch"
        :title="t.toolbar.clearSearch"
        @mousedown.prevent
        @click="clear"
      >
        <Icon icon="lucide:x" aria-hidden="true" />
      </button>
    </div>
    <button
      v-else
      v-bind="buttonAttrs"
      ref="button"
      type="button"
      class="t-page-toolbar-icon"
      :aria-label="labelText"
      :title="labelText"
      @click="open"
    >
      <Icon icon="lucide:search" aria-hidden="true" />
    </button>
  </div>
</template>

<style scoped>
.t-toolbar-search {
  display: inline-flex;
  flex: none;
  min-width: 0;
}
.t-toolbar-search__field {
  position: relative;
  display: flex;
  align-items: center;
  width: var(--t-toolbar-search-width, 200px);
  max-width: 100%;
}
.t-toolbar-search__icon {
  position: absolute;
  left: 10px;
  width: 14px;
  height: 14px;
  color: var(--tnzi-ai-text-tertiary);
  pointer-events: none;
}
.t-toolbar-search__input {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  height: var(--tnzi-ai-toolbar-control-size, 30px);
  padding: 0 10px 0 30px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: var(--tnzi-ai-field-font-size, 13px);
  outline: none;
  appearance: none;
  -webkit-appearance: none;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-toolbar-search__input.has-value {
  padding-right: 30px;
}
.t-toolbar-search__input::placeholder {
  color: var(--tnzi-ai-text-tertiary);
}
.t-toolbar-search__input:hover {
  border-color: var(--tnzi-ai-control-border-hover);
}
.t-toolbar-search__input:focus {
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}
/* The browser's own search decorations go: the clear button below is the
   package's, in the package's colours. */
.t-toolbar-search__input::-webkit-search-cancel-button,
.t-toolbar-search__input::-webkit-search-decoration,
.t-toolbar-search__input::-webkit-search-results-button,
.t-toolbar-search__input::-webkit-search-results-decoration {
  display: none;
  -webkit-appearance: none;
}
.t-toolbar-search__clear {
  position: absolute;
  right: 4px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 14px;
  cursor: pointer;
}
.t-toolbar-search__clear:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-toolbar-search__clear:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
@media (prefers-reduced-motion: reduce) {
  .t-toolbar-search__input {
    transition: none;
  }
}
</style>
