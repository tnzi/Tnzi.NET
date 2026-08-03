<script setup lang="ts">
/**
 * `TThemeSchemaSwitch` - three-state toggle (Light / Dark / Auto) that drives
 * the document's `dark` class and emits `change` so consumers can persist or
 * notify their own theme stores.
 *
 * Pure presentational - keeps zero coupling to any specific store so it can
 * live in headers, settings panels, login pages, etc.
 */
import { computed, ref, watch } from 'vue'
import type { ThemeMode } from '@tnzi/core/types'
import { applyThemeModeToDocument } from '../../theme/document'
import TButtonIcon from '../display/TButtonIcon.vue'

/**
 * Alias of core's {@link ThemeMode}. This component used to declare its own
 * `'light' | 'dark' | 'auto'` union, which is how the ecosystem ended up with
 * five hand-written copies of the same three values.
 */
export type ThemeSchema = ThemeMode

interface Props {
  /** Controlled value. When omitted, the component manages state internally. */
  value?: ThemeSchema
  /** Initial value when uncontrolled. */
  defaultValue?: ThemeSchema
  /** Translation function for accessible labels. */
  translate?: (key: string) => string
  /** Apply the `dark` class to `<html>` when value resolves to dark. */
  applyDocumentClass?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  value: undefined,
  defaultValue: 'auto',
  translate: undefined,
  applyDocumentClass: true,
})

const emit = defineEmits<{
  change: [value: ThemeSchema]
  'update:value': [value: ThemeSchema]
}>()

const internal = ref<ThemeSchema>(props.value ?? props.defaultValue)
const current = computed<ThemeSchema>(() =>
  props.value !== undefined ? props.value : internal.value,
)

function t(key: string, fallback: string): string {
  return props.translate ? props.translate(key) : fallback
}

const ICON: Record<ThemeSchema, string> = {
  light: 'mdi:weather-sunny',
  dark: 'mdi:weather-night',
  auto: 'mdi:theme-light-dark',
}
const NEXT: Record<ThemeSchema, ThemeSchema> = {
  light: 'dark',
  dark: 'auto',
  auto: 'light',
}

function cycle(): void {
  const next = NEXT[current.value]
  if (props.value === undefined) internal.value = next
  emit('change', next)
  emit('update:value', next)
}

watch(
  current,
  (s) => {
    if (!props.applyDocumentClass) return
    applyThemeModeToDocument(s)
  },
  { immediate: true },
)

const icon = computed(() => ICON[current.value])
const label = computed(() => t(`admin.themeSchema.${current.value}`, current.value))
</script>

<template>
  <TButtonIcon
    :icon="icon"
    :tooltip="label"
    class="t-theme-schema-switch"
    :data-schema="current"
    @click="cycle"
  />
</template>

<style scoped>
.t-theme-schema-switch {
  color: var(--tnzi-base-text);
}
</style>
