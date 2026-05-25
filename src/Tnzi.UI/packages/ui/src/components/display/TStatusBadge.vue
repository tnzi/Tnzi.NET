<script setup lang="ts">
/**
 * `TStatusBadge` — soybean-style status pill with semantic color mapping.
 *
 * Accepts either a boolean (true=success/false=danger) or a string value
 * mapped via the `mapping` prop. Falls back to neutral color for unknown
 * keys.
 *
 * Sunk from `@tnzi/ui-admin` in 0.2.x so site/chat/mobile and any other
 * consumer can use the same status pill without depending on the admin
 * framework. The admin wrapper at
 * `@tnzi/ui-admin/components/display/TStatusBadge.vue` injects the
 * `translatePageKey` helper as the `translate` prop so existing admin
 * call-sites keep their i18n behaviour.
 */
import { computed } from 'vue'
import { NTag } from 'naive-ui'

export type StatusType = 'success' | 'info' | 'warning' | 'error' | 'default'

interface Props {
  value: string | number | boolean | null | undefined
  /** Map values to {type, label}. Keys are stringified `value`. */
  mapping?: Record<string, { type: StatusType; label?: string; labelKey?: string }>
  /** Label override (skips mapping lookup). */
  label?: string
  /** i18n key for `label` (takes precedence over `label`). */
  labelKey?: string
  /** Type override (skips mapping lookup). */
  type?: StatusType
  size?: 'tiny' | 'small' | 'medium' | 'large'
  /**
   * Optional i18n resolver. Receives a key string, returns the translated
   * label, or an empty string when the key is unknown (in which case the
   * component falls back to the raw `label` / mapping label).
   *
   * Identity-default when omitted so the component is fully usable without
   * an i18n layer wired up.
   */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  mapping: () => ({}),
  label: undefined,
  labelKey: undefined,
  type: undefined,
  size: 'small',
  translate: undefined,
})

// Boolean default — resolves through the shared status dictionary so the
// label tracks the active locale (`admin.shared.status.{enabled,disabled}`)
// instead of being stuck on English "Enabled"/"Disabled".
const DEFAULT_BOOL_MAPPING: Record<string, { type: StatusType; labelKey: string; fallback: string }> = {
  true: { type: 'success', labelKey: 'admin.shared.status.enabled', fallback: 'Enabled' },
  // soybean parity — disabled rows render with the `warning` tone (orange)
  // rather than neutral grey so the negative state is visually obvious.
  false: { type: 'warning', labelKey: 'admin.shared.status.disabled', fallback: 'Disabled' },
}

// Humanise an i18n key into a readable last-segment fallback
// (`admin.shared.status.featured` → `Featured`). Used when no translator
// is supplied or the translator returns an empty string — better to
// render *something* than an empty pill.
function humaniseKey(key: string): string {
  const last = key.split('.').pop() ?? key
  const spaced = last.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]+/g, ' ')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

function resolveLabel(key: string | undefined, fallback: string): string {
  if (!key) return fallback
  if (!props.translate) return fallback || humaniseKey(key)
  const out = props.translate(key)
  return out || fallback || humaniseKey(key)
}

const resolved = computed<{ type: StatusType; label: string }>(() => {
  // Explicit label-key + type — short-circuit i18n resolution path.
  if (props.type && props.labelKey) {
    return { type: props.type, label: resolveLabel(props.labelKey, props.label ?? '') }
  }
  if (props.type && props.label) return { type: props.type, label: props.label }

  const key = String(props.value)
  const fromMapping = props.mapping[key]
  const isBool = typeof props.value === 'boolean'
  const boolEntry = isBool ? DEFAULT_BOOL_MAPPING[key] : undefined

  // Resolve type — explicit prop > mapping > bool default > 'default'.
  const finalType = props.type ?? fromMapping?.type ?? boolEntry?.type ?? 'default'

  // Resolve label with i18n priority chain:
  //  1. explicit prop.labelKey / prop.label
  //  2. mapping.labelKey / mapping.label
  //  3. boolean default i18n key
  //  4. raw string value
  let finalLabel: string
  if (props.labelKey) {
    finalLabel = resolveLabel(props.labelKey, props.label ?? key)
  } else if (props.label) {
    finalLabel = props.label
  } else if (fromMapping?.labelKey) {
    finalLabel = resolveLabel(fromMapping.labelKey, fromMapping.label ?? key)
  } else if (fromMapping?.label) {
    // Explicit raw mapping label wins over the bool default — keeps
    // back-compat for callers that intentionally use a non-standard label
    // (e.g. "Active" instead of "Enabled" for tenant rows).
    finalLabel = fromMapping.label
  } else if (boolEntry) {
    finalLabel = resolveLabel(boolEntry.labelKey, boolEntry.fallback)
  } else {
    finalLabel = key
  }

  return { type: finalType, label: finalLabel }
})
</script>

<template>
  <NTag :type="resolved.type === 'default' ? 'default' : resolved.type" :size="size" round>
    {{ resolved.label }}
  </NTag>
</template>
