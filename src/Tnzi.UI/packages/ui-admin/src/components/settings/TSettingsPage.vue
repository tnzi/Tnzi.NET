<template>
  <TDetailHost
    :state="detail"
    layout="side"
    :sections="navSections"
    :title="t('admin.modules.system.settings.title')"
    icon="mdi:cog-outline"
    :back="false"
    :translate="t"
  >
    <template #nav-header>
      <NInput
        v-model:value="searchQuery"
        size="small"
        clearable
        :placeholder="t('admin.modules.system.settings.search')"
      >
        <template #prefix><Icon icon="mdi:magnify" :width="16" /></template>
      </NInput>
    </template>
    <template #default="{ section }">
      <NSpin v-if="loading" class="t-settings-page__spin" />
      <template v-else>
        <TSettingsGroupPanel
          v-if="activeSchemaGroup(section)"
          :key="section ?? ''"
          :group="activeSchemaGroup(section)!"
          :save-group="saveGroup"
          :reset-group="resetGroup"
          @updated="onGroupUpdated"
          @refresh="() => load(true)"
        />
        <component
          :is="resolveCustomComponent(activeCustomSection(section)!)"
          v-else-if="activeCustomSection(section)"
          :key="section ?? ''"
        />
        <div v-else-if="section === ADVANCED_KEY && canViewParameters" class="t-settings-page__advanced">
          <Parameters />
        </div>
      </template>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, type Component } from 'vue'
import { NInput, NSpin } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { SettingsCenterGroupDto } from '@tnzi/core/services/system'
import TDetailHost from '../detail/TDetailHost.vue'
import TSettingsGroupPanel from './TSettingsGroupPanel.vue'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminSettingsConfig, type AdminSettingsSection } from '../../plugin/settingsConfig'
import { useDetail, type DetailSection } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { useSafeMessage } from '../../pages/_shared/safeMessage'
import { resolveBackendLabel, translatePageKey } from '../../pages/_shared/translate'

// 懒加载：避免 components barrel 经 eager import 拖入整个 TCrudPage/bridge 链
const Parameters = defineAsyncComponent(() => import('../../pages/system/Parameters.vue'))

const ADVANCED_KEY = 'advanced:parameters'

const bridge = createSystemBridge({ client: useAdminClient() })
const config = useAdminSettingsConfig()
const message = useSafeMessage()
const { can } = usePermissionGuard()

// The Advanced section embeds the raw Parameters/Dictionaries table, gated by
// system.parameter.view (its own Technical code). Users granted only per-module
// settings never see Advanced (and the backend blocks those endpoints anyway).
const canViewParameters = computed(() => can('system.parameter.view'))
const t = (key: string) => translatePageKey('', key)

const groups = ref<SettingsCenterGroupDto[]>([])
const loading = ref(true)

const saveGroup = (groupKey: string, changed: Record<string, string | null>) =>
  bridge.settingsCenter.saveGroup(groupKey, changed)
const resetGroup = (groupKey: string) => bridge.settingsCenter.resetGroup(groupKey)

const visibleGroups = computed(() => {
  const hidden = new Set(config?.hideGroups ?? [])
  return groups.value.filter((g) => !hidden.has(g.key))
})

const customSections = computed<AdminSettingsSection[]>(() =>
  [...(config?.sections ?? [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
)

const sections = computed<DetailSection[]>(() => [
  ...visibleGroups.value.map((g) => ({
    key: g.key,
    // Pre-resolve so a dictionary miss falls back to the backend display
    // name instead of TDetailLayout's humanised-key fallback.
    label: resolveBackendLabel(g.i18nKey, g.displayName),
    icon: g.icon ?? undefined,
    group: g.moduleName,
  })),
  ...customSections.value.map((s) => ({
    key: `custom:${s.key}`,
    label: s.label,
    icon: s.icon,
    group: s.group ?? 'App',
  })),
  ...(canViewParameters.value
    ? [
        {
          key: ADVANCED_KEY,
          label: 'admin.modules.system.settings.advancedParameters',
          icon: 'mdi:tune',
          group: 'admin.modules.system.settings.advancedGroup',
        },
      ]
    : []),
])

// Global settings search: filters ONLY the left nav (matched groups show, clear
// restores). The `useDetail` engine below keeps the full `sections` list so the
// `?section=` deep link + default resolution stay stable even while filtering.
const searchQuery = ref('')

// key → lowercase searchable text (group label + module + every field label for
// schema groups; label + group for custom sections; the label for Advanced).
const searchIndex = computed<Record<string, string>>(() => {
  const idx: Record<string, string> = {}
  for (const g of visibleGroups.value) {
    const parts = [resolveBackendLabel(g.i18nKey, g.displayName), g.moduleName]
    for (const f of g.fields) parts.push(resolveBackendLabel(f.i18nKey, f.label))
    idx[g.key] = parts.join(' ').toLowerCase()
  }
  for (const s of customSections.value) {
    idx[`custom:${s.key}`] = `${s.label} ${s.group ?? ''}`.toLowerCase()
  }
  if (canViewParameters.value) {
    idx[ADVANCED_KEY] = t('admin.modules.system.settings.advancedParameters').toLowerCase()
  }
  return idx
})

const navSections = computed<DetailSection[]>(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return sections.value
  const idx = searchIndex.value
  return sections.value.filter((s) => (idx[s.key] ?? '').includes(q))
})

// The single detail engine, page mode: the active section is two-way bound to
// the `?section=` query key (deep-linkable + Back/Forward step through panels).
// Definitions load async; the default defers to the first SCHEMA group (not the
// always-present Advanced placeholder) and re-resolves the moment groups arrive.
// No record / no back — a top-level settings page reached from the menu.
const detail = useDetail({
  mode: 'page',
  sectionUrl: true,
  sections,
  defaultSection: () => visibleGroups.value[0]?.key,
})

function activeSchemaGroup(section: string | null): SettingsCenterGroupDto | undefined {
  return section ? visibleGroups.value.find((g) => g.key === section) : undefined
}

function activeCustomSection(section: string | null): AdminSettingsSection | undefined {
  if (!section?.startsWith('custom:')) return undefined
  const key = section.slice('custom:'.length)
  return customSections.value.find((s) => s.key === key)
}

// Contract: `AdminSettingsSection.component` is either a component object
// (incl. a `defineAsyncComponent(...)` result) used as-is, or a plain loader
// function (`() => import('./Section.vue')`) wrapped in `defineAsyncComponent`.
// Resolutions are cached per section key — calling `defineAsyncComponent` on
// every render would produce a fresh definition and remount the panel.
const customComponentCache = new Map<string, Component>()
function resolveCustomComponent(s: AdminSettingsSection): Component {
  const cached = customComponentCache.get(s.key)
  if (cached) return cached
  const resolved =
    typeof s.component === 'function'
      ? defineAsyncComponent(s.component as () => Promise<{ default: Component }>)
      : s.component
  customComponentCache.set(s.key, resolved)
  return resolved
}

function onGroupUpdated(updated: SettingsCenterGroupDto): void {
  groups.value = groups.value.map((g) => (g.key === updated.key ? updated : g))
}

// `silent` = realtime-triggered re-fetch (another session changed a group):
// keep the current panel mounted (no spinner flash); the fresh group DTO
// flows into the panel via its `props.group` watch and re-hydrates in place.
async function load(silent = false): Promise<void> {
  if (!silent) loading.value = true
  try {
    groups.value = await bridge.settingsCenter.getDefinitions()
  } catch (error) {
    if (silent) return
    groups.value = []
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    if (!silent) loading.value = false
    // No manual seed needed: `useSectionRoute` re-resolves the default the moment
    // the (async) `sections` list changes, picking the first schema group.
  }
}

onMounted(() => {
  void load()
})
</script>

<style scoped>
.t-settings-page__spin {
  display: flex;
  justify-content: center;
  padding: 48px 0;
}
/* Parameters is a TCrudPage (mode=page) — it needs an unbroken flex height
   chain to fill the TDetailLayout side panel (overflow:hidden flex column). */
.t-settings-page__advanced {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
</style>
