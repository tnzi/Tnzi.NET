<template>
  <button
    type="button"
    class="t-theme-switcher"
    :aria-label="ariaLabel"
    @click="cycle"
  >
    <svg
      v-if="modelValue === 'light'"
      class="t-theme-switcher__icon t-theme-switcher__icon--light"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
    </svg>
    <svg
      v-else-if="modelValue === 'dark'"
      class="t-theme-switcher__icon t-theme-switcher__icon--dark"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </svg>
    <svg
      v-else
      class="t-theme-switcher__icon t-theme-switcher__icon--auto"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="9" />
      <path d="M12 3v18" />
      <path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor" />
    </svg>
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'

type ThemeMode = 'light' | 'dark' | 'auto'

interface Props {
  modelValue: ThemeMode
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: ThemeMode]
}>()

const ariaLabel = computed(() => {
  switch (props.modelValue) {
    case 'light':
      return 'Switch to dark theme'
    case 'dark':
      return 'Switch to auto theme'
    default:
      return 'Switch to light theme'
  }
})

function cycle() {
  const next: Record<ThemeMode, ThemeMode> = {
    light: 'dark',
    dark: 'auto',
    auto: 'light',
  }
  emit('update:modelValue', next[props.modelValue])
}
</script>

<style scoped>
.t-theme-switcher {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  background-color: transparent;
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: 6px;
  color: var(--tnzi-base-text, #1f2937);
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}
.t-theme-switcher:hover {
  background-color: var(--tnzi-base-bg-subtle, #f3f4f6);
}
.t-theme-switcher__icon {
  display: block;
}
</style>
