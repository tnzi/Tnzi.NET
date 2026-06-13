<template>
  <TDetailLayout
    layout="side"
    :sections="sections"
    :active-section="activeSection"
    :title="t('admin.modules.system.settings.title')"
    icon="mdi:cog-outline"
    :translate="t"
    @update:active-section="(k: string) => (activeSection = k)"
  >
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
        />
        <component
          :is="resolveCustomComponent(activeCustomSection(section)!)"
          v-else-if="activeCustomSection(section)"
          :key="section ?? ''"
        />
        <div v-else-if="section === ADVANCED_KEY" class="t-settings-page__advanced">
          <Parameters />
        </div>
      </template>
    </template>
  </TDetailLayout>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, type Component } from 'vue'
import { NSpin } from 'naive-ui'
import type { SettingsCenterGroupDto } from '@tnzi/core/services/system'
import TDetailLayout from '../detail/TDetailLayout.vue'
import TSettingsGroupPanel from './TSettingsGroupPanel.vue'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminSettingsConfig, type AdminSettingsSection } from '../../plugin/settingsConfig'
import type { DetailSection } from '../../headless/useDetail'
import { useSafeMessage } from '../../pages/_shared/safeMessage'
import { resolveBackendLabel, translatePageKey } from '../../pages/_shared/translate'

// 懒加载：避免 components barrel 经 eager import 拖入整个 TCrudPage/bridge 链
const Parameters = defineAsyncComponent(() => import('../../pages/system/Parameters.vue'))

const ADVANCED_KEY = 'advanced:parameters'

const bridge = createSystemBridge({ client: useAdminClient() })
const config = useAdminSettingsConfig()
const message = useSafeMessage()
const t = (key: string) => translatePageKey('', key)

const groups = ref<SettingsCenterGroupDto[]>([])
const loading = ref(true)
const activeSection = ref<string | null>(null)

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
  {
    key: ADVANCED_KEY,
    label: 'admin.modules.system.settings.advancedParameters',
    icon: 'mdi:tune',
    group: 'admin.modules.system.settings.advancedGroup',
  },
])

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

async function load(): Promise<void> {
  loading.value = true
  try {
    groups.value = await bridge.settingsCenter.getDefinitions()
  } catch (error) {
    groups.value = []
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loading.value = false
    if (!activeSection.value) {
      activeSection.value = sections.value[0]?.key ?? null
    }
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
