<script setup lang="ts">
/**
 * `TStatusBadge` admin wrapper.
 *
 * The component implementation lives in `@tnzi/ui/components/display`
 * (sunk in 0.2.x). This wrapper just injects the admin-side
 * `translatePageKey` helper so existing admin call-sites pick up i18n
 * resolution without passing the translator explicitly.
 *
 * New code outside the admin shell should import directly from
 * `@tnzi/ui`:
 *
 *   import { TStatusBadge } from '@tnzi/ui'
 */
import { TStatusBadge as TStatusBadgeBase, type StatusType } from '@tnzi/ui'
import { translatePageKey } from '../../i18n/translate'

interface Props {
  value: string | number | boolean | null | undefined
  mapping?: Record<string, { type: StatusType; label?: string; labelKey?: string }>
  label?: string
  labelKey?: string
  type?: StatusType
  size?: 'tiny' | 'small' | 'medium' | 'large'
}

withDefaults(defineProps<Props>(), {
  mapping: () => ({}),
  label: undefined,
  labelKey: undefined,
  type: undefined,
  size: 'small',
})

const adminTranslate = (key: string): string => translatePageKey('', key)
</script>

<template>
  <TStatusBadgeBase
    :value="value"
    :mapping="mapping"
    :label="label"
    :label-key="labelKey"
    :type="type"
    :size="size"
    :translate="adminTranslate"
  />
</template>
