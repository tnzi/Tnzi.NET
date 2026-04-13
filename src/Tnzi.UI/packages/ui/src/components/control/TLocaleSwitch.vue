<template>
  <div ref="rootEl" class="t-locale-switch">
    <button
      type="button"
      class="t-locale-switch__trigger"
      :aria-expanded="open"
      @click="toggle"
    >
      <svg
        class="t-locale-switch__icon"
        viewBox="0 0 24 24"
        width="16"
        height="16"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <circle cx="12" cy="12" r="10" />
        <path d="M2 12h20" />
        <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
      </svg>
      <span class="t-locale-switch__label">{{ currentLabel }}</span>
    </button>
    <ul v-if="open" class="t-locale-switch__menu" role="listbox">
      <li
        v-for="opt in options"
        :key="opt.value"
        class="t-locale-switch__option"
        :class="{ 't-locale-switch__option--active': opt.value === modelValue }"
        role="option"
        :aria-selected="opt.value === modelValue"
        @click="select(opt.value)"
      >
        {{ opt.label }}
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

interface LocaleOption {
  value: string
  label: string
}

interface Props {
  modelValue: string
  options: LocaleOption[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const open = ref(false)

const currentLabel = computed(() => {
  const found = props.options.find((o) => o.value === props.modelValue)
  return found ? found.label : props.modelValue
})

function toggle() {
  open.value = !open.value
}

function select(value: string) {
  emit('update:modelValue', value)
  open.value = false
}

function handleDocumentClick(event: MouseEvent) {
  if (!open.value) return
  const target = event.target as Node | null
  const root = rootEl.value
  if (root && target && !root.contains(target)) {
    open.value = false
  }
}

const rootEl = ref<HTMLElement | null>(null)

onMounted(() => {
  if (typeof document !== 'undefined') {
    document.addEventListener('click', handleDocumentClick)
  }
})

onBeforeUnmount(() => {
  if (typeof document !== 'undefined') {
    document.removeEventListener('click', handleDocumentClick)
  }
})
</script>

<style scoped>
.t-locale-switch {
  position: relative;
  display: inline-block;
}
.t-locale-switch__trigger {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 36px;
  padding: 0 12px;
  background-color: transparent;
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: 6px;
  color: var(--tnzi-base-text, #1f2937);
  cursor: pointer;
  font-size: 14px;
}
.t-locale-switch__trigger:hover {
  background-color: var(--tnzi-base-bg-subtle, #f3f4f6);
}
.t-locale-switch__menu {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  min-width: 140px;
  margin: 0;
  padding: 4px 0;
  list-style: none;
  background-color: var(--tnzi-base-bg, #fff);
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: 6px;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.1);
  z-index: 100;
}
.t-locale-switch__option {
  padding: 8px 12px;
  font-size: 14px;
  color: var(--tnzi-base-text, #1f2937);
  cursor: pointer;
}
.t-locale-switch__option:hover {
  background-color: var(--tnzi-base-bg-subtle, #f3f4f6);
}
.t-locale-switch__option--active {
  color: var(--tnzi-primary-500, #3b82f6);
  font-weight: 600;
}
</style>
