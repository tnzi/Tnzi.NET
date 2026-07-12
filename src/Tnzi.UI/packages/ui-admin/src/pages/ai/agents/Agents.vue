<template>
  <!--
    Agents — production-grade card page.
      - top health KPI strip (bridge.agents.getHealth): Total / Healthy / Unhealthy / Disabled
      - TCardPage grid: each agent card shows robot icon + name + enabled badge +
        execution-mode badge + provider·model + truncated description + 3 tier
        badges (quality/latency/cost) + persona marker
      - card actions: View (router push to detail) / Edit (modal) / Clone / Delete
      - full CRUD via the agent-config form schema; keyword + provider /
        execution-mode advanced search.
    Sibling `agent-config.ts` owns the pure data (form schema, search fields,
    option lists, label/type helpers). i18n via translatePageKey('ai.agents', …).
  -->
  <TCardPage
    :state="crud"
    mode="page"
    :title="t('title')"
    :title-help="t('banner')"
    :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
    :search-fields="searchFields"
    :search-placeholder="t('search.placeholder')"
    :form-modal-width="760"
    :translate="t"
  >
    <!-- Health KPI strip — between the page header and the card grid. -->
    <template #kpis>
      <TKpiRow class="ai-agent-page__kpis">
        <TKpiCard
          v-for="kpi in kpiCards"
          :key="kpi.key"
          class="ai-agent-page__kpi"
          :label="t(kpi.labelKey)"
          :value="kpi.value"
          :icon="kpi.icon"
          :tone="kpi.tone"
        />
      </TKpiRow>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="agentFormSchemaWithProviders"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>

    <template #card="{ item }">
      <TEntityCard clickable @click="openDetail(item)">
        <div class="flex items-center gap-8px mb-6px">
          <span class="ai-agent-card__icon">
            <TSvgIcon icon="mdi:robot-happy-outline" :size="18" color="var(--tnzi-primary, #6d5ce7)" />
          </span>
          <span class="ai-agent-card__name flex-1">{{ item.name }}</span>
          <NTag size="small" :type="item.isEnabled ? 'success' : 'warning'" :bordered="false">
            {{ item.isEnabled ? t('badge.enabled') : t('badge.disabled') }}
          </NTag>
        </div>

        <div class="flex flex-wrap items-center gap-6px mb-6px">
          <NTag size="small" type="info" :bordered="false">
            {{ t(executionModeKey(item.executionMode)) }}
          </NTag>
          <NTag v-if="item.personaId" size="small" :bordered="false">
            {{ t('badge.persona') }}
          </NTag>
        </div>

        <div class="ai-agent-card__provider font-mono">
          {{ item.provider }}{{ item.model ? ` · ${item.model}` : '' }}
        </div>

        <div class="ai-agent-card__desc">{{ item.description || t('noDescription') }}</div>

        <div class="flex flex-wrap gap-4px mt-8px">
          <NTag
            size="small"
            :type="tierTagType(item.qualityTier)"
            :bordered="false"
            :title="`${t('tier.quality')}: ${item.qualityTier ?? 0}`"
          >
            {{ t('tier.quality') }} · {{ t(tierLabelKey(item.qualityTier)) }}
          </NTag>
          <NTag
            size="small"
            :type="tierTagType(item.latencyTier)"
            :bordered="false"
            :title="`${t('tier.latency')}: ${item.latencyTier ?? 0}`"
          >
            {{ t('tier.latency') }} · {{ t(tierLabelKey(item.latencyTier)) }}
          </NTag>
          <NTag
            size="small"
            :type="tierTagType(item.costTier)"
            :bordered="false"
            :title="`${t('tier.cost')}: ${item.costTier ?? 0}`"
          >
            {{ t('tier.cost') }} · {{ t(tierLabelKey(item.costTier)) }}
          </NTag>
        </div>

        <template #actions>
          <NButton v-if="crud.canUpdate" size="small" ghost @click="crud.openEdit(item)">
            {{ t('actions.edit') }}
          </NButton>
          <NPopconfirm v-if="can('ai.agent.create')" @positive-click="cloneOne(item)">
            <template #trigger>
              <NButton size="small" ghost :loading="cloningId === item.id">
                {{ t('actions.clone') }}
              </NButton>
            </template>
            {{ t('cloneConfirm') }}
          </NPopconfirm>
          <NPopconfirm v-if="crud.canDelete" @positive-click="removeOne(item)">
            <template #trigger>
              <NButton size="small" type="error" ghost>{{ t('actions.delete') }}</NButton>
            </template>
            {{ t('deleteConfirm') }}
          </NPopconfirm>
        </template>
      </TEntityCard>
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NPopconfirm, NTag, useMessage } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TKpiCard, { type TKpiCardTone } from '../../../components/data/TKpiCard.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import type { FormSchemaItem } from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import {
  agentFormSchema,
  agentSearchFields,
  executionModeKey,
  tierLabelKey,
  tierTagType,
} from './agent-config'
import type {
  AgentDto,
  AgentHealthSummaryDto,
  CreateAgentDto,
  UpdateAgentDto,
  ProviderOptionDto,
} from '@tnzi/core/services/ai'

const t = (key: string) => translatePageKey('ai.agents', key)

const router = useRouter()
const message = (() => {
  try { return useMessage() } catch { return null }
})()

const bridge = createAiBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()

const crud = useCrudPage<AgentDto>({
  pageId: 'ai.agents',
  permission: 'ai.agent',
  columns: [], // card page renders via #card slot; column defs unused
  rowKey: (a) => a.id,
  fetchData: (query) => bridge.agents.fetch(query),
  createData: (data) => bridge.agents.create(data as CreateAgentDto),
  updateData: (id, data) => bridge.agents.update(String(id), data as UpdateAgentDto),
  deleteData: (ids) => bridge.agents.delete(ids.map(String)),
})

// --- provider dropdown (API-driven) ----------------------------------------
// The static agent-config provider list uses lowercase slugs which won't match
// the configured provider *names* the runtime resolves against. Load the real
// enabled providers and inject them into the create-form + search provider
// selects (falling back to the static list if the API yields nothing).
const providerOptions = ref<ProviderOptionDto[]>([])
async function loadProviderOptions(): Promise<void> {
  try {
    providerOptions.value = await bridge.providers.getOptions()
  } catch {
    providerOptions.value = []
  }
}
loadProviderOptions().catch(() => undefined)

function withProviderOptions(schema: FormSchemaItem[]): FormSchemaItem[] {
  if (providerOptions.value.length === 0) return schema
  const opts = providerOptions.value.map((p) => ({ label: `${p.name} · ${p.providerType}`, value: p.name }))
  return schema.map((f) => (f.key === 'provider' ? { ...f, options: opts } : f))
}

const agentFormSchemaWithProviders = computed(() => withProviderOptions(agentFormSchema))
const searchFields = computed(() => withProviderOptions(agentSearchFields))

// --- health KPI strip ------------------------------------------------------
const health = ref<AgentHealthSummaryDto | null>(null)

async function loadHealth(): Promise<void> {
  try {
    health.value = await bridge.agents.getHealth()
  } catch {
    health.value = null
  }
}
loadHealth().catch(() => undefined)

const kpiCards = computed<Array<{ key: string; labelKey: string; icon: string; tone: TKpiCardTone; value: number }>>(() => {
  const h = health.value
  return [
    { key: 'total', labelKey: 'kpi.total', icon: 'mdi:robot-outline', tone: 'default', value: h?.totalAgents ?? 0 },
    { key: 'healthy', labelKey: 'kpi.healthy', icon: 'mdi:check-circle-outline', tone: 'success', value: h?.healthyAgents ?? 0 },
    { key: 'unhealthy', labelKey: 'kpi.unhealthy', icon: 'mdi:alert-circle-outline', tone: 'error', value: h?.unhealthyAgents ?? 0 },
    { key: 'disabled', labelKey: 'kpi.disabled', icon: 'mdi:pause-circle-outline', tone: 'warning', value: h?.disabledAgents ?? 0 },
  ]
})

// --- card actions ----------------------------------------------------------
function openDetail(item: AgentDto): void {
  // By route NAME: a literal '/admin/...' path breaks under a custom basePath.
  router.push({ name: 'ai.agents.detail', params: { id: item.id } }).catch(() => undefined)
}

const cloningId = ref<string | null>(null)

async function cloneOne(item: AgentDto): Promise<void> {
  cloningId.value = item.id
  try {
    await bridge.agents.clone(item.id)
    message?.success(t('cloneSuccess'))
    await crud.refresh()
    await loadHealth()
  } catch (e) {
    message?.error((e as Error).message || t('cloneError'))
  } finally {
    cloningId.value = null
  }
}

async function removeOne(item: AgentDto): Promise<void> {
  await crud.handleDelete([item.id])
  await loadHealth()
}
</script>

<style scoped>
.ai-agent-card__icon {
  display: inline-flex;
}
.ai-agent-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-agent-card__provider {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-agent-card__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 36px;
}
</style>
