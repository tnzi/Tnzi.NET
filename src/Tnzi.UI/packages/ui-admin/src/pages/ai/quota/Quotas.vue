<template>
  <!--
    Usage limits: everyone's defaults, the users who used AI (each opens their
    own limits in a drawer), and the tenant's cost budget. Three tabs because
    each list wants the full residual height.
  -->
  <TTabsPage :title="t('title')" :help="t('titleHelp')" :translate="t" :sections="tabs" default-section="defaults">
    <template #actions="{ active }">
      <!-- Configure anyone, including a user who has not used AI yet (they are
           not in the list until their first turn). -->
      <div v-if="active === 'users' && hasIdentity" class="ai-usage-page__configure">
        <TUserSelector
          :value="null"
          :fetcher="userFetcher"
          :placeholder="t('users.configurePlaceholder')"
          size="small"
          class="!w-260px max-w-full"
          @update:value="onConfigure"
        />
      </div>
    </template>

    <!-- ── Defaults ─────────────────────────────────────────────── -->
    <template #defaults>
      <TUsageLimitDefaultsPanel :bridge="bridge.usageLimits" />
    </template>

    <!-- ── Users who used AI ─────────────────────────────────────── -->
    <template #users>
      <TItemPage
        :state="crud"
        mode="page"
        :show-header="false"
        :show-default-search="false"
        :show-create="false"
        :translate="t"
      >
        <template #empty><TEmpty :text="t('users.empty')" /></template>
        <template v-if="hasIdentity" #toolbarLeft>
          <TUserSelector
            :value="filterUserId"
            :fetcher="userFetcher"
            :placeholder="t('users.filterPlaceholder')"
            size="small"
            clearable
            class="!w-240px max-w-full"
            @update:value="onFilterUser"
          />
        </template>
        <template #item="{ item }">
          <TItemCard
            :title="userLabel(item.userId)"
            :subtitle="item.userId"
            icon="mdi:account-outline"
            :icon-tone="rowTone(item)"
            :tags="rowTags(item)"
            clickable
            @click="openUser(item.userId)"
          >
            <template #meta>
              <span class="ai-usage-row__meta">
                <TSvgIcon icon="mdi:history" :size="13" />
                {{ item.lastUsedAt ? t('user.lastUsed', { time: formatDateTime(item.lastUsedAt) }) : t('user.neverUsed') }}
              </span>
            </template>
            <div class="ai-usage-row__windows">
              <div v-for="w in item.windows" :key="w.window" class="ai-usage-row__window">
                <div class="ai-usage-row__window-head">
                  <span>{{ w.window === UsageLimitWindow.Weekly ? t('window.weekly') : t('window.session') }}</span>
                  <span class="font-mono">
                    {{ w.limitUsd != null ? `${formatUsd(w.usedUsd)} / ${formatUsd(w.limitUsd)}` : formatUsd(w.usedUsd) }}
                  </span>
                </div>
                <NProgress
                  v-if="w.limitUsd != null"
                  type="line"
                  :percentage="barPercent(w.usedPercent)"
                  :height="6"
                  :show-indicator="false"
                  :status="w.exceeded ? 'error' : w.warningReached ? 'warning' : 'success'"
                />
                <span v-else class="ai-usage-row__unlimited">{{ t('mode.unlimited') }}</span>
              </div>
            </div>
          </TItemCard>
        </template>
      </TItemPage>
    </template>

    <!-- ── Budget (KPIs + per-agent spend breakdown) ─────────────── -->
    <template #budget>
      <!-- No summary (still loading, or the read failed) shows dashes, never a
           green "Within Budget, $0.00": that is what a quiet month looks like. -->
      <NAlert
        v-if="budgetError"
        type="error"
        :show-icon="false"
        class="ai-quota-budget__error"
        data-test="budget-error"
      >
        {{ t('budget.loadFailed', { message: budgetError }) }}
      </NAlert>
      <TKpiRow cols="1 s:2 m:4" class="ai-quota-budget__kpis">
        <TKpiCard :label="t('budget.spend')" :value="budget ? `$${formatBudgetUsd(budget.currentSpendUsd)}` : EMPTY_DASH" tone="success" />
        <TKpiCard
          :label="t('budget.limit')"
          :value="!budget ? EMPTY_DASH : budget.budgetLimitUsd > 0 ? `$${formatBudgetUsd(budget.budgetLimitUsd)}` : t('budget.noLimit')"
        />
        <TKpiCard :label="t('budget.usage')" :value="formatPercent(budget?.usagePercentage)">
          <template v-if="budget" #footer>
            <NProgress
              type="line"
              :percentage="usagePercent"
              :status="progressStatus"
              :show-indicator="false"
              :height="6"
            />
          </template>
        </TKpiCard>
        <TKpiCard :label="t('budget.status')" :value="budgetStatusLabel" :tone="statusTagType" />
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
          :data="budget?.byAgent ?? []"
          :loading="budgetLoading"
          :row-key="(r: AgentSpendDto) => r.agentId ?? r.agentName"
          :pagination="false"
          :flex-height="true"
          size="small"
          :empty-text="t('budget.noSpend')"
        />
      </NCard>
    </template>

    <template #overlays>
      <TDetailHost :state="userDetail" :title="userDetailTitle" :width="760" :footer="false" :translate="t">
        <template #default="{ data }">
          <TUserUsageLimitsPanel
            v-if="detailUserId(data)"
            :user-id="detailUserId(data)"
            :bridge="bridge.usageLimits"
            @updated="onUserUpdated"
          />
        </template>
      </TDetailHost>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
/**
 * Usage limits page: everyone's defaults, the users who used AI, and the
 * tenant's cost budget.
 *
 * Limits are API-equivalent USD over two windows (a 5-hour session window that
 * starts with the first message, and a weekly window); a user who reaches one
 * is told when it resets rather than shown an error. Each user row opens that
 * user's own limits (`?user=view:<id>`), and "Configure a user" reaches someone
 * who has not used AI yet. The two panels are the same components a host can
 * mount on its own settings and user pages.
 */
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, ref, watch } from 'vue'
import { NAlert, NButton, NCard, NProgress, NTag, type DataTableColumns } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { TEmpty, TSvgIcon } from '@tnzi/ui'
import { TKpiCard, TKpiRow } from '../../../components/data'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'
import TItemPage from '../../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../../../components/data/TItemCard.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TUserSelector from '../../../components/forms/TUserSelector.vue'
import type { SelectorOption } from '../../../components/forms/_selector-factory'
import TUsageLimitDefaultsPanel from '../../../components/ai/TUsageLimitDefaultsPanel.vue'
import TUserUsageLimitsPanel from '../../../components/ai/TUserUsageLimitsPanel.vue'
import { barPercent, formatUsd } from '../../../components/ai/usage-limit-format'
import { useCrudPage } from '../../../headless/useCrudPage'
import { useDetail } from '../../../headless/useDetail'
import { useModuleAvailability } from '../../../headless/useModuleAvailability'
import { createAiBridge, UsageLimitWindow } from '../../../services/bridges/ai-bridge'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import { usageUserColumns } from './usage-limit-config'
import type { BudgetSummaryDto, AgentSpendDto, UserUsageSummaryDto, UserUsageLimitsDto } from '@tnzi/core/services/ai'

const t = makePageTranslator('ai.quota')

const client = useAdminClient()
const bridge = createAiBridge({ client })
const idBridge = createIdentityBridge({ client })
const { has } = useModuleAvailability()
const hasIdentity = computed(() => has('identity'))

const tabs: TabSection[] = [
  { name: 'defaults', label: t('tabs.defaults'), scroll: true, displayDirective: 'show' },
  { name: 'users', label: t('tabs.users'), displayDirective: 'show' },
  { name: 'budget', label: t('tabs.budget'), displayDirective: 'show' },
]

// --- users who used AI ------------------------------------------------------
const crud = useCrudPage<UserUsageSummaryDto>({
  pageId: 'ai.quota.users',
  columns: usageUserColumns,
  rowKey: (r) => r.userId,
  fetchData: (query) => bridge.usageLimits.fetchUsers(query),
  // The page opens users through its own `?user=` drawer; the list itself has no overlay.
  detailUrl: false,
})

const filterUserId = ref<string | null>(null)
function onFilterUser(value: unknown): void {
  filterUserId.value = typeof value === 'string' && value ? value : null
  crud.setFilters({ userId: filterUserId.value ?? undefined })
  void crud.refresh()
}

/**
 * Names for the rows on screen. The meter only knows user ids; the names come
 * from Identity, one read per id, remembered across pages. Without Identity
 * (or a read refused) the row shows its id, which is what it is.
 */
const userNames = ref<Record<string, string | null>>({})
function userLabel(userId: string): string {
  return userNames.value[userId] || `${userId.slice(0, 8)}…`
}
watch(
  () => crud.items.value.map((i) => i.userId),
  (ids) => {
    if (!hasIdentity.value) return
    const missing = ids.filter((id) => !(id in userNames.value))
    if (missing.length === 0) return
    userNames.value = { ...userNames.value, ...Object.fromEntries(missing.map((id) => [id, null])) }
    for (const id of missing) {
      idBridge.users.getById(id).then(
        (u) => {
          const label = u?.userName ? (u.email ? `${u.userName} (${u.email})` : u.userName) : null
          userNames.value = { ...userNames.value, [id]: label }
        },
        () => undefined,
      )
    }
  },
  { immediate: true },
)

const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const res = await idBridge.users.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: keyword.trim(),
      sortField: undefined,
      sortOrder: null,
      filters: {},
    })
    return res.items.map((u) => {
      const label = u.email ? `${u.userName} (${u.email})` : u.userName
      userNames.value = { ...userNames.value, [u.id]: label }
      return { label, value: u.id }
    })
  } catch {
    return []
  }
}

function rowTone(item: UserUsageSummaryDto): ItemCardTone {
  if (item.windows.some((w) => w.exceeded)) return 'error'
  if (item.windows.some((w) => w.warningReached)) return 'warning'
  return 'default'
}

function rowTags(item: UserUsageSummaryDto): ItemCardTag[] {
  const tags: ItemCardTag[] = []
  if (item.windows.some((w) => w.exceeded)) tags.push({ label: t('user.exceeded'), type: 'error' })
  else if (item.windows.some((w) => w.warningReached)) tags.push({ label: t('user.warning'), type: 'warning' })
  if (item.hasCustomPolicy) tags.push({ label: t('user.custom'), type: 'primary' })
  return tags
}

// --- one user's limits (drawer) --------------------------------------------
interface UserTarget {
  userId: string
}

const userDetail = useDetail<UserTarget>({
  mode: 'drawer',
  url: 'user',
  getId: (target) => target.userId,
  loadData: async (id) => ({ userId: String(id) }),
})

function openUser(userId: string): void {
  void userDetail.open('view', { userId })
}

function onConfigure(value: unknown): void {
  if (typeof value === 'string' && value) openUser(value)
}

function detailUserId(data: unknown): string {
  return (data as UserTarget | null)?.userId ?? ''
}

const userDetailTitle = computed(() => {
  const id = userDetail.data.value?.userId
  return id ? t('users.drawerTitle', { name: userLabel(id) }) : t('users.drawerTitleEmpty')
})

/** The row reflects what the drawer changed (settings chip, reset windows). */
function onUserUpdated(limits: UserUsageLimitsDto): void {
  const index = crud.items.value.findIndex((i) => i.userId === limits.userId)
  if (index < 0) {
    void crud.refresh()
    return
  }
  const next = [...crud.items.value]
  next[index] = {
    userId: limits.userId,
    hasCustomPolicy: limits.hasCustomPolicy,
    windows: limits.usage.windows,
    lastUsedAt: limits.usage.lastUsedAt ?? null,
  }
  crud.items.value = next
}

// --- budget dashboard -------------------------------------------------------
// `null` = no summary to show (not loaded yet, or the read failed). It is never
// replaced by a zero summary: a zero summary reads as "Within budget, $0.00".
const budget = ref<BudgetSummaryDto | null>(null)
const budgetError = ref<string | null>(null)
const budgetLoading = ref(false)

const usagePercent = computed(() => {
  const pct = Math.round((budget.value?.usagePercentage ?? 0) * 1000) / 10
  return Math.max(0, Math.min(100, pct))
})

// BudgetStatus enum → number. The backend serialises it as the PascalCase
// member NAME (WithinBudget / WarningThreshold / BudgetExceeded / Indeterminate);
// a numeric ordinal is still accepted. Normalising to a number keeps the
// `budget.statusValue.<n>` i18n key + the tone comparison below working.
const BUDGET_STATUS_VALUE: Record<string, number> = {
  '0': 0, '1': 1, '2': 2, '3': 3,
  WithinBudget: 0, WarningThreshold: 1, BudgetExceeded: 2, Indeterminate: 3,
}
const budgetStatusValue = computed(() => BUDGET_STATUS_VALUE[String(budget.value?.status ?? 0)] ?? 0)
const budgetStatusLabel = computed(() =>
  budget.value ? t(`budget.statusValue.${budgetStatusValue.value}`) : budgetError.value ? t('budget.unavailable') : EMPTY_DASH,
)

// 3 = Indeterminate: the spend could not be measured (cost tracking off or no
// model rate), so the 0 USD is not a green "within budget" - paint it as a warning.
const progressStatus = computed<'success' | 'warning' | 'error'>(() =>
  budgetStatusValue.value === 2 ? 'error' : budgetStatusValue.value === 1 || budgetStatusValue.value === 3 ? 'warning' : 'success',
)

const statusTagType = computed<'success' | 'warning' | 'error' | undefined>(() =>
  budget.value ? progressStatus.value : budgetError.value ? 'error' : undefined,
)

function formatBudgetUsd(value: number | null | undefined): string {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) return '0.00'
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function formatPercent(ratio: number | null | undefined): string {
  const n = typeof ratio === 'number' ? ratio : Number(ratio)
  if (!Number.isFinite(n)) return EMPTY_DASH
  return `${Math.round(n * 1000) / 10}%`
}

// Per-agent spend breakdown columns (share fee + count + % of total).
const agentSpendColumns = computed<DataTableColumns<AgentSpendDto>>(() => {
  const total = (budget.value?.byAgent ?? []).reduce((sum, a) => sum + (a.spendUsd ?? 0), 0)
  return [
    {
      key: 'agentName',
      title: t('budget.agentName'),
      render: (row) => row.agentName || row.agentId || EMPTY_DASH,
    },
    {
      key: 'spendUsd',
      title: t('budget.agentSpend'),
      align: 'right',
      render: (row) => `$${formatBudgetUsd(row.spendUsd)}`,
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
          : EMPTY_DASH,
    },
  ]
})

async function refreshBudget(): Promise<void> {
  budgetLoading.value = true
  try {
    // No time range → backend defaults to the current calendar month.
    const result = await bridge.usageLimits.getBudgetSummary()
    budget.value = result ? { ...result, byAgent: result.byAgent ?? [] } : null
    budgetError.value = result ? null : t('budget.noSummary')
  } catch (err) {
    // A failed budget read must not block the other tabs, and must not be
    // shown as a zero spend either: the tab says it could not be read.
    budget.value = null
    budgetError.value = err instanceof Error && err.message ? err.message : String(err)
  } finally {
    budgetLoading.value = false
  }
}

onMounted(() => {
  refreshBudget().catch(() => undefined)
})
</script>

<style scoped>
.ai-usage-page__configure {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ai-usage-row__meta {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.ai-usage-row__windows {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 8px 20px;
  margin-top: 8px;
  max-width: 560px;
}
.ai-usage-row__window {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.ai-usage-row__window-head {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.ai-usage-row__unlimited {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
/* KPI strip (TKpiRow) sits above the breakdown table inside the Budget tab
   pane - fixed height, the table below claims the rest. */
.ai-quota-budget__kpis {
  flex-shrink: 0;
  margin-bottom: 12px;
}
.ai-quota-budget__error {
  flex-shrink: 0;
  margin-bottom: 12px;
}
</style>
