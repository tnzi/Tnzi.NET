<template>
  <NRadioGroup :value="mode" size="small" class="t-fin-view" @update:value="setMode">
    <NRadioButton value="owner">
      <TSvgIcon icon="mdi:briefcase-outline" :size="14" class="t-fin-view__icon" />
      {{ label('owner') }}
    </NRadioButton>
    <NRadioButton value="accountant">
      <TSvgIcon icon="mdi:calculator-variant-outline" :size="14" class="t-fin-view__icon" />
      {{ label('accountant') }}
    </NRadioButton>
  </NRadioGroup>
</template>

<script setup lang="ts">
/**
 * `TFinanceViewToggle` - the owner / accountant layer switch.
 *
 * Deliberately a plain, droppable control rather than something bolted into a
 * finance-specific shell: the layer is a property of the whole finance area,
 * so a consumer app should be able to surface it wherever its own navigation
 * makes sense (page header, settings panel, user menu). The built-in Reports
 * page mounts one so the mechanism is reachable out of the box.
 *
 * Switching is non-destructive and instantaneous: the owner layer simply hides
 * the double-entry pages from the sidebar. Nothing is filtered from the data,
 * no wording changes (see `useFinanceViewMode` for why terminology must not
 * follow the viewer), and the routes stay registered, so a bookmark into the
 * journals still resolves.
 */
import { NRadioButton, NRadioGroup } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useFinanceViewMode, type FinanceViewMode } from '../../headless/useFinanceViewMode'

const props = defineProps<{
  /** i18n lookup; keys are relative to `finance.viewMode.*`. */
  translate?: (key: string) => string
}>()

const { mode, setMode } = useFinanceViewMode()

const FALLBACK: Record<FinanceViewMode, string> = {
  owner: 'Business',
  accountant: 'Accounting',
}

function label(key: FinanceViewMode): string {
  const translated = props.translate?.(`viewMode.${key}`)
  // `makePageTranslator` echoes an unknown key back; fall back to English
  // rather than rendering `viewMode.owner`.
  if (translated && !translated.includes(`viewMode.${key}`)) return translated
  return FALLBACK[key]
}
</script>

<style scoped>
.t-fin-view__icon {
  margin-right: 4px;
  vertical-align: -2px;
}
</style>
