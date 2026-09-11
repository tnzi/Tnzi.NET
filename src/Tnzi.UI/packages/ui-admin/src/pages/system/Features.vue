<template>
  <TTabsPage
    v-model:section="active"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    :sections="tabs"
    default-section="definitions"
  >
    <template #definitions>
      <FeatureDefinitionsTab />
    </template>
    <template #values>
      <FeatureValuesTab />
    </template>
    <template #usage>
      <FeatureUsageTab />
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
/**
 * Feature flags - one screen, three tabs, in the order a flag lives its life:
 *
 *   Definitions - what flags exist (catalogue; card grid)
 *   Values      - what each flag resolves to for a scope (Global / Tenant / …)
 *   Usage       - who is asking, and how the answer trends
 *
 * Before the second and third tabs existed the module could DEFINE a flag but
 * never turn it on for anyone: the value and usage endpoints had no client.
 * Keeping all three behind the single `system.features` route (permission
 * `feature.view`, gated on the Feature module being loaded) means the sidebar
 * gains nothing and the URL deep-links via `?section=`.
 *
 * The tabs hide the shell's page header (TTabsPage owns the title bar) but keep
 * the shell's list toolbar - keyword search, Create, refresh - so nothing has to
 * be re-plumbed up here. Tabs stay mounted (`displayDirective: 'show'`) so a
 * value set on the Values tab is still there when the operator flips back.
 *
 * ★ Two list shells on one route: each tab's `useCrudPage` sets its own
 *   `detailUrl` key (`definition` / `value`). With the shared default `detail`
 *   key, opening an editor in one tab writes `?detail=edit:<id>`, the OTHER tab
 *   cannot resolve that id, clears the key, and the editor closes on open.
 */
import { ref } from 'vue'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import { makePageTranslator } from '../_shared/translate'
import FeatureDefinitionsTab from './features/FeatureDefinitionsTab.vue'
import FeatureValuesTab from './features/FeatureValuesTab.vue'
import FeatureUsageTab from './features/FeatureUsageTab.vue'

const t = makePageTranslator('system.features')

const tabs: TabSection[] = [
  { name: 'definitions', label: t('tabs.definitions'), displayDirective: 'show' },
  { name: 'values', label: t('tabs.values'), displayDirective: 'show' },
  { name: 'usage', label: t('tabs.usage'), displayDirective: 'show', scroll: true },
]
const active = ref('definitions')
</script>
