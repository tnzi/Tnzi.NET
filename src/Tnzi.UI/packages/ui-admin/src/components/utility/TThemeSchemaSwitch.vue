<script setup lang="ts">
/**
 * `TThemeSchemaSwitch` - three-state toggle (Light / Dark / Auto).
 *
 * Where a `@tnzi/ui` theme context is installed (any app on
 * `createTnziUi()` / `createTnziUiAdmin()`), the switch drives that context:
 * it shows `settings.mode` and changes it through `setMode`, and the context
 * alone writes the document's `dark` class. Everything that reads the
 * context - the naive theme and overrides in `TAdminAppRoot`, the login
 * page's own dark styling - therefore moves with the class. Writing the class
 * here as well would let the two disagree: a dark document over light naive
 * controls.
 *
 * With no context installed it falls back to writing the `dark` class itself
 * (`applyDocumentClass`), following the OS while on `'auto'`.
 *
 * Either way it emits `change` / `update:value`, so a consumer can persist
 * the choice.
 */
import { computed, inject, ref, watch } from 'vue'
import type { ThemeMode } from '@tnzi/core/types'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { applyThemeModeToDocument, resolveThemeMode } from '@tnzi/ui/theme'
import TButtonIcon from '../display/TButtonIcon.vue'
import { translateChromeKey } from '../../i18n/translate'

/**
 * Alias of core's {@link ThemeMode}. This component used to declare its own
 * `'light' | 'dark' | 'auto'` union, which is how the ecosystem ended up with
 * five hand-written copies of the same three values.
 */
export type ThemeSchema = ThemeMode

interface Props {
  /**
   * Controlled value. With a theme context installed, a value given here is
   * also set on the context.
   */
  value?: ThemeSchema
  /**
   * Initial value when uncontrolled and no theme context is installed (with a
   * context, the context's current mode is the initial value).
   */
  defaultValue?: ThemeSchema
  /** Translation function for accessible labels. */
  translate?: (key: string) => string
  /**
   * Without a theme context: apply the `dark` class to `<html>` when the value
   * resolves to dark. Ignored when a context is installed, which writes the
   * class itself.
   */
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

const themeContext = inject<ThemeContext | null>(THEME_CONTEXT_KEY, null)

const internal = ref<ThemeSchema>(props.value ?? props.defaultValue)
const current = computed<ThemeSchema>(() => {
  if (props.value !== undefined) return props.value
  return themeContext ? themeContext.settings.value.mode : internal.value
})

/**
 * The app's `translate` when it knows the key, else the admin locale
 * (`admin.themeSchema.*`), else English.
 */
function t(key: string, fallback: string): string {
  const hit = props.translate?.(key)
  if (hit && hit !== key) return hit
  return translateChromeKey(key, fallback)
}

const FALLBACK_LABEL: Record<ThemeSchema, string> = {
  light: 'Light',
  dark: 'Dark',
  auto: 'Follow system',
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
  if (props.value === undefined) {
    if (themeContext) themeContext.setMode(next)
    else internal.value = next
  }
  emit('change', next)
  emit('update:value', next)
}

if (themeContext) {
  // A controlled value reaches the context; the context writes the class.
  watch(
    () => props.value,
    (value) => {
      if (value !== undefined && themeContext.settings.value.mode !== value) themeContext.setMode(value)
    },
    { immediate: true },
  )
} else {
  watch(
    current,
    (s, _prev, onCleanup) => {
      if (!props.applyDocumentClass) return
      applyThemeModeToDocument(s)
      if (s !== 'auto') return
      // 'auto' follows the OS for as long as it stays selected, not only the OS
      // scheme at the moment it was picked. The listener goes when the value
      // changes or the component unmounts.
      const { cleanup } = resolveThemeMode('auto', (resolved) => applyThemeModeToDocument(resolved))
      if (cleanup) onCleanup(cleanup)
    },
    { immediate: true },
  )
}

const icon = computed(() => ICON[current.value])
const label = computed(() => t(`admin.themeSchema.${current.value}`, FALLBACK_LABEL[current.value]))
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
