/**
 * `defineEnumMeta` - one declaration of an enum's UI metadata that yields the
 * three shapes pages repeatedly hand-build: locale-reactive select `options`, a
 * `label(value)` accessor, and a `badgeMapping` ready for `<TStatusBadge>`.
 *
 * Replaces the per-app "options list + byValue label lookup + status→tone map"
 * boilerplate (and the lazy-getter locale-reactivity trick consumers keep
 * rediscovering) with a single call. Labels resolve through the same admin
 * translator the pages use, so `options` / `label` re-render on locale change.
 *
 *   const MatterStatus = defineEnumMeta<string>(
 *     [
 *       { value: 'Active', labelKey: 'matter.status.active', tone: 'success' },
 *       { value: 'Closed', labelKey: 'matter.status.closed', tone: 'default' },
 *     ],
 *     makePageTranslator('crm.matters'),
 *   )
 *   // <NSelect :options="MatterStatus.options.value" />
 *   // <TStatusBadge :value="row.status" :mapping="MatterStatus.badgeMapping" />
 *   // {{ MatterStatus.label(row.status) }}
 */
import { computed, type ComputedRef } from 'vue'
import type { StatusType } from '@tnzi/ui'
import { translatePageKey } from '../i18n/translate'

export interface EnumMetaSpec<V extends string | number> {
  value: V
  /** Fallback label - used when `labelKey` is not supplied. */
  label?: string
  /** i18n key resolved via the translator (locale-reactive). Wins over `label`. */
  labelKey?: string
  /** `<TStatusBadge>` tone for this value. */
  tone?: StatusType
}

export interface EnumMeta<V extends string | number> {
  /** `<NSelect :options>` with locale-resolved labels (reactive). */
  options: ComputedRef<Array<{ label: string; value: V }>>
  /** Display label for a value (reactive when read in a render). */
  label: (value: V | null | undefined) => string
  /** `<TStatusBadge>` tone for a value. */
  tone: (value: V | null | undefined) => StatusType | undefined
  /** Static mapping for `<TStatusBadge :mapping>` (keyed by `String(value)`). */
  badgeMapping: Record<string, { type: StatusType; label?: string; labelKey?: string }>
  /** The raw specs - e.g. to render a legend. */
  specs: EnumMetaSpec<V>[]
}

export function defineEnumMeta<V extends string | number>(
  specs: EnumMetaSpec<V>[],
  translate: (key: string) => string = (k) => translatePageKey('', k),
): EnumMeta<V> {
  const byValue = new Map<V, EnumMetaSpec<V>>(specs.map((s) => [s.value, s]))
  const resolve = (spec: EnumMetaSpec<V> | undefined): string =>
    spec ? (spec.labelKey ? translate(spec.labelKey) : spec.label ?? String(spec.value)) : ''

  const options = computed(() => specs.map((s) => ({ value: s.value, label: resolve(s) })))
  const label = (value: V | null | undefined): string => (value == null ? '' : resolve(byValue.get(value)))
  const tone = (value: V | null | undefined): StatusType | undefined =>
    value == null ? undefined : byValue.get(value)?.tone

  const badgeMapping: Record<string, { type: StatusType; label?: string; labelKey?: string }> = {}
  for (const s of specs) {
    badgeMapping[String(s.value)] = { type: s.tone ?? 'default', label: s.label, labelKey: s.labelKey }
  }

  return { options, label, tone, badgeMapping, specs }
}
