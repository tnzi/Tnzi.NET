<script setup lang="ts" generic="T extends string">
/**
 * TMultiSelect - a dropdown that picks several.
 *
 * The chosen ones are chips in the field, each with its own remove button; the
 * rest are in a list that opens under it, each with a checkbox, and the list
 * stays open while picking. With `searchFrom` options or more the list has a
 * search box, which takes focus when the list opens. Chosen values that are
 * not among the options (a project since removed) stay in the value: they are
 * not this control's to drop.
 *
 * The list stays in the field's own place in the page rather than on an
 * overlay layer, so a dialog around it keeps Tab inside and its scrolling
 * carries the list along. Keyboard: the toggle button opens and closes it,
 * Tab moves through the search and the checkboxes (Space ticks one), Escape
 * closes the list, gives focus back to the toggle and goes no further, so a
 * dialog around it stays open. A press outside closes it.
 *
 * Every word is the caller's (`label`, `placeholder`, `searchPlaceholder`,
 * `emptyText`, `removeLabel` with `{name}`).
 *
 * Every change of the value is also a native `change` that bubbles, as with
 * `TSelect`, so a form that notes edits by listening on a container sees it:
 * a checkbox fires its own (once, from the checkbox), and removing a chip
 * fires one on the field (`[data-multi-field]`). The search box's own
 * `change` (a browser fires one when it loses focus after typing) is not the
 * value's and stays inside.
 */
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import type { TMultiSelectOption } from './options'

const props = withDefaults(
  defineProps<{
    modelValue: readonly T[]
    options: readonly TMultiSelectOption<T>[]
    /** The field's accessible name (the toggle's and the list's). */
    label: string
    /** Shown when nothing is chosen. */
    placeholder?: string
    searchPlaceholder?: string
    /** Shown when no option matches, or there are none. */
    emptyText?: string
    /** A chip's remove button's accessible name; `{name}` is the option's label. */
    removeLabel?: string
    disabled?: boolean
    /** From this many options the list has a search. */
    searchFrom?: number
  }>(),
  { placeholder: '', searchPlaceholder: '', emptyText: '', removeLabel: '{name}', disabled: false, searchFrom: 7 },
)
const emit = defineEmits<{ 'update:modelValue': [value: T[]] }>()

const open = ref(false)
const query = ref('')
const root = ref<HTMLElement | null>(null)
const search = ref<HTMLInputElement | null>(null)
const toggleButton = ref<HTMLButtonElement | null>(null)
const field = ref<HTMLElement | null>(null)

const chosen = computed(() => props.options.filter((option) => props.modelValue.includes(option.value)))
const searchable = computed(() => props.options.length >= props.searchFrom)
const shown = computed(() => {
  const needle = query.value.trim().toLocaleLowerCase()
  return needle ? props.options.filter((option) => option.label.toLocaleLowerCase().includes(needle)) : props.options
})

function setChosen(value: T, on: boolean): void {
  const without = props.modelValue.filter((item) => item !== value)
  emit('update:modelValue', on ? [...without, value] : without)
}

/** A chip's remove button: nothing native fires for it, so the field fires the `change`. */
function removeChip(value: T): void {
  if (!props.modelValue.includes(value)) return
  setChosen(value, false)
  field.value?.dispatchEvent(new Event('change', { bubbles: true }))
}

function toggleOpen(): void {
  if (!props.disabled) open.value = !open.value
}

function onOutside(event: PointerEvent): void {
  if (root.value && !event.composedPath().includes(root.value)) open.value = false
}

function onKeydown(event: KeyboardEvent): void {
  if (!open.value || event.key !== 'Escape' || event.isComposing) return
  event.preventDefault()
  event.stopPropagation()
  open.value = false
  toggleButton.value?.focus()
}

watch(open, async (isOpen) => {
  if (isOpen) {
    document.addEventListener('pointerdown', onOutside, true)
    query.value = ''
    await nextTick()
    search.value?.focus()
  } else {
    document.removeEventListener('pointerdown', onOutside, true)
  }
})
onBeforeUnmount(() => document.removeEventListener('pointerdown', onOutside, true))
</script>

<template>
  <div ref="root" class="t-multi-select" :class="{ 'is-open': open, 'is-disabled': disabled }" @keydown="onKeydown">
    <div ref="field" class="t-input t-multi-select__field" data-multi-field @click="toggleOpen">
      <span v-if="chosen.length === 0" class="t-multi-select__placeholder">{{ placeholder }}</span>
      <span v-for="option in chosen" :key="option.value" class="t-multi-select__chip" data-multi-chip @click.stop>
        <span class="t-multi-select__chip-name">{{ option.label }}</span>
        <button
          type="button"
          class="t-multi-select__chip-remove"
          :aria-label="removeLabel.replace('{name}', option.label)"
          :disabled="disabled"
          @click="removeChip(option.value)"
        >
          <Icon icon="lucide:x" aria-hidden="true" />
        </button>
      </span>
      <button
        ref="toggleButton"
        type="button"
        class="t-multi-select__toggle"
        :aria-label="label"
        :aria-expanded="open"
        :disabled="disabled"
        data-multi-toggle
        @click.stop="toggleOpen"
      >
        <Icon icon="lucide:chevron-down" aria-hidden="true" />
      </button>
    </div>
    <div v-if="open" class="t-multi-select__list" role="group" :aria-label="label" data-multi-list>
      <input
        v-if="searchable"
        ref="search"
        v-model="query"
        type="search"
        class="t-input t-multi-select__search"
        :placeholder="searchPlaceholder"
        :aria-label="searchPlaceholder || label"
        data-multi-search
        @change.stop
      />
      <div class="t-multi-select__options t-ai-scroll">
        <label v-for="option in shown" :key="option.value" class="t-multi-select__option">
          <input
            type="checkbox"
            :checked="modelValue.includes(option.value)"
            v-bind="option.attrs"
            @change="setChosen(option.value, ($event.target as HTMLInputElement).checked)"
          />
          <span class="t-multi-select__option-name">{{ option.label }}</span>
        </label>
        <p v-if="shown.length === 0" class="t-multi-select__empty">{{ emptyText }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-multi-select {
  position: relative;
  min-width: 0;
}
.t-multi-select__field {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  min-height: 34px;
  height: auto;
  padding: 3px 30px 3px 6px;
  cursor: pointer;
}
.t-multi-select.is-disabled .t-multi-select__field {
  cursor: default;
  opacity: 0.6;
}
.t-multi-select__placeholder {
  padding: 0 4px;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-multi-select__chip {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  max-width: 100%;
  height: 24px;
  padding: 0 2px 0 8px;
  border-radius: var(--tnzi-ai-radius-sm, 6px);
  background: var(--tnzi-ai-hover);
  font-size: 12.5px;
  cursor: default;
}
.t-multi-select__chip-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-multi-select__chip-remove,
.t-multi-select__toggle {
  /* Its own text metrics, not the page reset's (which differ: 1.15 and the
     system font in most resets, `inherit` under a utility preflight). */
  font-family: inherit;
  font-size: 100%;
  line-height: 1.5;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: 0;
  background: none;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
}
.t-multi-select__chip-remove {
  /* Its own text metrics, not the page reset's (which differ: 1.15 and the
     system font in most resets, `inherit` under a utility preflight). */
  font-family: inherit;
  font-size: 100%;
  line-height: 1.5;
  flex: none;
  width: 20px;
  height: 20px;
  border-radius: var(--tnzi-ai-radius-xs, 4px);
}
.t-multi-select__chip-remove:hover {
  background: var(--tnzi-ai-hover);
  color: inherit;
}
.t-multi-select__toggle {
  position: absolute;
  top: 0;
  right: 4px;
  width: 24px;
  height: 34px;
}
.t-multi-select__toggle :deep(svg) {
  transition: transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-multi-select.is-open .t-multi-select__toggle :deep(svg) {
  transform: rotate(180deg);
}
.t-multi-select__chip-remove:focus-visible,
.t-multi-select__toggle:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 0;
}
.t-multi-select__list {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 5;
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  box-shadow: 0 6px 20px rgb(0 0 0 / 12%);
}
.t-multi-select__search {
  flex: none;
}
.t-multi-select__options {
  position: relative;
  max-height: 208px;
  overflow-y: auto;
}
.t-multi-select__option {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 32px;
  padding: 4px 8px;
  border-radius: var(--tnzi-ai-radius-sm, 6px);
  font-size: 13px;
  cursor: pointer;
}
.t-multi-select__option:hover {
  background: var(--tnzi-ai-hover);
}
.t-multi-select__option input {
  flex: none;
  width: 15px;
  height: 15px;
  margin: 0;
  accent-color: var(--tnzi-ai-accent);
}
.t-multi-select__option-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-multi-select__empty {
  margin: 0;
  padding: 8px;
  font-size: 12.5px;
  color: var(--tnzi-ai-text-tertiary);
}
</style>
