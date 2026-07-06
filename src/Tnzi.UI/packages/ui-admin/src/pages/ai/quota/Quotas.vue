<template>
  <!--
    Quotas — budget cost dashboard + per-user AI token quota CRUD.

    Top:  a budget dashboard strip fed by `quota.getBudgetSummary()`
          (GET /admin/quotas/budget/summary - defaults to the current month
          when no time range is supplied). KPI cards (spend / limit / usage %
          with a progress bar / status badge) + a per-agent spend breakdown.
    Body: the quota rules table (TCrudPage). Create/edit upsert via
          quota.setQuota (idempotent, keyed on userId). Delete is not
          supported by the bridge - `deleteData` is omitted so the delete
          affordance is hidden automatically.
  -->
  <!--
    Tabbed layout: a normal screen height can't show the per-agent spend
    breakdown table AND the quota-rules table stacked. Two tabs give each list
    the full residual height; the rules tab is the default. TTabsPage owns the
    white page-header bar (title + the user-ID search) and the tab surface.
  -->
  <TTabsPage :title="t('title')" :translate="t" :sections="tabs" default-section="rules">
    <!-- User-ID search in the page-header bar (C2). The quota query endpoint
         filters on `userId` (free-text searchText is ignored by the bridge),
         so the input drives `filters.userId` directly. Rules tab only. -->
    <template #actions="{ active }">
      <div v-if="active === 'rules'" class="ai-quota-page__search">
        <NInput
          v-model:value="userIdQuery"
          size="small"
          clearable
          :placeholder="t('search.placeholder')"
          class="ai-quota-page__search-input"
          @keydown.enter="onUserIdSearch"
          @clear="onUserIdClear"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="16" /></template>
        </NInput>
        <NButton size="small" type="primary" @click="onUserIdSearch">
          {{ t('admin.crud.search') }}
        </NButton>
      </div>
    </template>

    <!-- ── Quota rules (card list) ───────────────────────────────── -->
    <template #rules>
      <!-- show-header=false suppresses the shell's header bar - title +
           search live on the outer TTabsPage header (otherwise the shell
           falls back to the route meta title and renders a duplicate
           bar). Create is upsert-by-userId; there is no per-row delete. -->
      <TCardPage
        :state="crud"
        mode="page"
        :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
        :form-modal-width="640"
        :show-header="false"
        :translate="t"
      >
        <template #card="{ item }">
          <TEntityCard clickable @click="crud.openEdit(item)">
            <div class="flex items-center justify-between gap-8px mb-8px">
              <code class="ai-quota-card__uid" :title="item.userId">{{ shortId(item.userId) }}</code>
              <div class="flex items-center gap-4px flex-shrink-0">
                <TStatusBadge :value="warningLevelValue(item)" :mapping="warningLevelBadgeMapping" size="small" />
                <TStatusBadge :value="Boolean(item.isEnabled)" :mapping="enabledBadgeMapping" size="small" />
              </div>
            </div>

            <div class="ai-quota-card__limits">
              <div class="ai-quota-card__limit">
                <span class="ai-quota-card__limit-label">{{ t('columns.dailyTokenLimit') }}</span>
                <span class="ai-quota-card__limit-value">{{ formatLimit(item.dailyTokenLimit) }}</span>
              </div>
              <div class="ai-quota-card__limit">
                <span class="ai-quota-card__limit-label">{{ t('columns.monthlyTokenLimit') }}</span>
                <span class="ai-quota-card__limit-value">{{ formatLimit(item.monthlyTokenLimit) }}</span>
              </div>
            </div>

            <div class="ai-quota-card__usage">
              <div class="ai-quota-card__usage-head">
                <span>{{ t('card.dailyUsage') }}</span>
                <span class="font-mono">{{ formatTokens(item.currentDailyUsage) }} · {{ formatPercent(item.dailyUsagePercentage) }}</span>
              </div>
              <NProgress
                type="line"
                :percentage="percentValue(item.dailyUsagePercentage)"
                :height="6"
                :show-indicator="false"
                :status="usageStatus(item.dailyUsagePercentage)"
              />
            </div>
            <div class="ai-quota-card__usage">
              <div class="ai-quota-card__usage-head">
                <span>{{ t('card.monthlyUsage') }}</span>
                <span class="font-mono">{{ formatTokens(item.currentMonthlyUsage) }} · {{ formatPercent(item.monthlyUsagePercentage) }}</span>
              </div>
              <NProgress
                type="line"
                :percentage="percentValue(item.monthlyUsagePercentage)"
                :height="6"
                :show-indicator="false"
                :status="usageStatus(item.monthlyUsagePercentage)"
              />
            </div>

            <template #actions>
              <span class="ai-quota-card__modified mr-auto">{{ formatModified(item) }}</span>
              <NButton size="small" ghost @click="crud.openEdit(item)">{{ t('actions.edit') }}</NButton>
            </template>
          </TEntityCard>
        </template>

        <template #form="{ formData, mode }">
          <TFormSchemaRenderer
            :schema="quotaFormSchema"
            :model="(formData ?? {}) as Record<string, unknown>"
            :readonly="mode === 'view'"
            :translate="t"
            :columns="2"
          />
        </template>
      </TCardPage>
    </template>

    <!-- ── Budget (KPIs + per-agent spend breakdown) ─────────────── -->
    <template #budget>
      <TKpiRow cols="1 s:2 m:4" class="ai-quota-budget__kpis">
        <TKpiCard :label="t('budget.spend')" :value="`$${formatUsd(budget.currentSpendUsd)}`" tone="success" />
        <TKpiCard
          :label="t('budget.limit')"
          :value="budget.budgetLimitUsd > 0 ? `$${formatUsd(budget.budgetLimitUsd)}` : t('budget.noLimit')"
        />
        <TKpiCard :label="t('budget.usage')" :value="formatPercent(budget.usagePercentage)">
          <template #footer>
            <NProgress
              type="line"
              :percentage="usagePercent"
              :status="progressStatus"
              :show-indicator="false"
              :height="6"
            />
          </template>
        </TKpiCard>
        <TKpiCard :label="t('budget.status')" :value="t(`budget.statusValue.${budgetStatusValue}`)" :tone="statusTagType" />
      </TKpiRow>

      <!-- Per-agent spend breakdown - fills the residual height + scrolls. -->
      <NCard size="small" :bordered="false" class="ai-quota-budget__breakdown t-table-card t-tab-card" :title="t('budget.byAgent')">
        <template #header-extra>
          <NButton size="small" tertiary :loading="budgetLoading" @click="refreshBudget">
            <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
            {{ t('budget.refresh') }}
          </NButton>
        </template>
        <TResponsiveTable
          :columns="agentSpendColumns"
          :data="budget.byAgent ?? []"
          :loading="budgetLoading"
          :row-key="(r: AgentSpendDto) => r.agentId ?? r.agentName"
          :pagination="false"
          :flex-height="true"
          size="small"
          :empty-text="t('budget.noSpend')"
        />
      </NCard>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import { NButton, NCard, NInput, NProgress, NTag, type DataTableColumns } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { TKpiCard, TKpiRow } from '../../../components/data'
import { TSvgIcon } from '@tnzi/ui'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { makePageTranslator } from '../../_shared/translate'
import {
  quotaColumns,
  quotaFormSchema,
  formatLimit,
  formatTokens,
  percentValue,
  warningLevelBadgeMapping,
  enabledBadgeMapping,
  warningLevelValue,
} from './quota-config'
import type {
  UserQuotaDto,
  SetQuotaDto,
  BudgetSummaryDto,
  AgentSpendDto,
} from '@tnzi/core/services/ai'

const t = makePageTranslator('ai.quota')

const bridge = createAiBridge({ client: useAdminClient() })

// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
// Rules is a single flex-height table; budget is a KPI strip + a flex-height
// breakdown table - neither pane owns its own scroll. Both keep every pane
// mounted (displayDirective 'show') so state survives tab switches.
const tabs: TabSection[] = [
  { name: 'rules', label: t('tabs.rules'), displayDirective: 'show' },
  { name: 'budget', label: t('tabs.budget'), displayDirective: 'show' },
]

const crud = useCrudPage<UserQuotaDto>({
  pageId: 'ai.quota',
  columns: quotaColumns,
  rowKey: (q) => q.id,
  fetchData: (query) => bridge.quota.fetch(query),
  createData: (data) => bridge.quota.create(data as SetQuotaDto),
  updateData: (_id, data) => bridge.quota.update(String(_id), data as SetQuotaDto),
  // No deleteData - quota.delete is not supported by the bridge (the delete
  // affordance is hidden automatically when deleteData is omitted).
})

// --- card helpers -----------------------------------------------------------
/** GUID truncated to its first 8 chars for the card header (full in `title`). */
function shortId(value: string | null | undefined): string {
  const full = value ?? ''
  if (!full) return '—'
  return full.length > 8 ? `${full.slice(0, 8)}…` : full
}

/** Colour a usage progress bar by how close it is to the limit. */
function usageStatus(ratio: number | null | undefined): 'default' | 'warning' | 'error' {
  const pct = percentValue(ratio)
  if (pct >= 90) return 'error'
  if (pct >= 70) return 'warning'
  return 'default'
}

/** Last-modified line shown in the card footer (falls back to an em dash). */
function formatModified(row: UserQuotaDto): string {
  return formatDateTime(
    (row as { lastModificationTime?: string | null }).lastModificationTime,
    { fallback: '—' },
  )
}

// Header user-ID search - drives the query's `filters.userId` (the backend
// quota query has no free-text keyword; userId is the supported filter).
const userIdQuery = ref('')
function onUserIdSearch(): void {
  crud.setFilters({ userId: userIdQuery.value.trim() || undefined })
  void crud.refresh()
}
function onUserIdClear(): void {
  userIdQuery.value = ''
  crud.setFilters({ userId: undefined })
  void crud.refresh()
}

// --- budget dashboard -------------------------------------------------------
const emptyBudget: BudgetSummaryDto = {
  periodStart: '',
  periodEnd: '',
  currentSpendUsd: 0,
  budgetLimitUsd: 0,
  usagePercentage: 0,
  status: 'WithinBudget',
  byAgent: [],
}

const budget = ref<BudgetSummaryDto>({ ...emptyBudget })
const budgetLoading = ref(false)

const usagePercent = computed(() => {
  const pct = Math.round((budget.value.usagePercentage ?? 0) * 1000) / 10
  return Math.max(0, Math.min(100, pct))
})

// BudgetStatus enum → number. The backend serialises it as the PascalCase
// member NAME (JsonStringEnumConverter: WithinBudget / WarningThreshold /
// BudgetExceeded); a numeric ordinal is still accepted for backward
// compatibility. Normalising to a number keeps the `budget.statusValue.<n>`
// i18n key + the tone comparison below working.
const BUDGET_STATUS_VALUE: Record<string, number> = {
  '0': 0, '1': 1, '2': 2,
  WithinBudget: 0, WarningThreshold: 1, BudgetExceeded: 2,
}
const budgetStatusValue = computed(() => BUDGET_STATUS_VALUE[String(budget.value.status ?? 0)] ?? 0)

const progressStatus = computed<'success' | 'warning' | 'error'>(() =>
  budgetStatusValue.value === 2 ? 'error' : budgetStatusValue.value === 1 ? 'warning' : 'success',
)

const statusTagType = computed<'success' | 'warning' | 'error'>(() => progressStatus.value)

function formatUsd(value: number | null | undefined): string {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) return '0.00'
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function formatPercent(ratio: number | null | undefined): string {
  const n = typeof ratio === 'number' ? ratio : Number(ratio)
  if (!Number.isFinite(n)) return '—'
  return `${Math.round(n * 1000) / 10}%`
}

// Per-agent spend breakdown columns (share fee + count + % of total).
const agentSpendColumns = computed<DataTableColumns<AgentSpendDto>>(() => {
  const total = budget.value.byAgent.reduce((sum, a) => sum + (a.spendUsd ?? 0), 0)
  return [
    {
      key: 'agentName',
      title: t('budget.agentName'),
      render: (row) => row.agentName || row.agentId || '—',
    },
    {
      key: 'spendUsd',
      title: t('budget.agentSpend'),
      align: 'right',
      render: (row) => `$${formatUsd(row.spendUsd)}`,
    },
    {
      key: 'requestCount',
      title: t('budget.requestCount'),
      align: 'right',
      render: (row) => (row.requestCount ?? 0).toLocaleString('en-US'),
    },
    {
      key: 'share',
      title: t('budget.share'),
      align: 'right',
      render: (row) =>
        total > 0
          ? h(
              NTag,
              { size: 'small', bordered: false },
              { default: () => `${Math.round(((row.spendUsd ?? 0) / total) * 1000) / 10}%` },
            )
          : '—',
    },
  ]
})

async function refreshBudget(): Promise<void> {
  budgetLoading.value = true
  try {
    // No time range → backend defaults to the current calendar month.
    const result = await bridge.quota.getBudgetSummary()
    budget.value = result ? { ...emptyBudget, ...result, byAgent: result.byAgent ?? [] } : { ...emptyBudget }
  } catch {
    // Budget fetch failure must not block the quota table.
    budget.value = { ...emptyBudget }
  } finally {
    budgetLoading.value = false
  }
}

onMounted(() => {
  refreshBudget().catch(() => undefined)
})
</script>

<style scoped>
/* Page-header user-ID search (rules tab only). */
.ai-quota-page__search {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ai-quota-page__search-input {
  width: 240px;
  max-width: 100%;
}
@media (max-width: 640px) {
  .ai-quota-page__search { flex-wrap: wrap; }
  .ai-quota-page__search-input { width: 100%; }
}
/* KPI strip (TKpiRow) sits above the breakdown table inside the Budget tab
   pane - fixed height, the table below claims the rest. The responsive grid is
   TKpiRow's own; this only adds the separating gap (the pane has no row gap). */
.ai-quota-budget__kpis {
  flex-shrink: 0;
  margin-bottom: 12px;
}

/* ── Quota rule card ──────────────────────────────────────────────────── */
.ai-quota-card__uid {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-quota-card__limits {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-bottom: 10px;
}
.ai-quota-card__limit {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.ai-quota-card__limit-label {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.ai-quota-card__limit-value {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ai-quota-card__usage {
  margin-bottom: 8px;
}
.ai-quota-card__usage-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 4px;
}
.ai-quota-card__modified {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  align-self: center;
}
</style>
