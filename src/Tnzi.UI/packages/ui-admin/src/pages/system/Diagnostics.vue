<template>
  <!--
    Diagnostics - surfaces `/admin/diagnostics/*` (ships with HostingModule).
    Three tabs:
      • Modules - loaded ModuleManifest list (assembly, deps, services, events, options)
      • Controllers - every Mvc controller route the runtime exposes
      • Exceptions - summary KPIs + recent ring buffer + clear

    Read-mostly. No edits, no per-row modal - this is an ops console, not a CRUD page.
  -->
  <TTabsPage
    :title="t('title')"
    :translate="t"
    :sections="tabs"
    default-section="modules"
  >
    <template #kpis>
      <TKpiRow cols="1 s:3">
        <TKpiCard :label="t('kpi.modules')" :value="modules.length" icon="mdi:view-grid-outline" />
        <TKpiCard :label="t('kpi.controllers')" :value="controllerResult?.totalCount ?? null" icon="mdi:router-network" />
        <TKpiCard :label="t('kpi.exceptions', { minutes: windowMinutes })" :value="summary?.totalCount ?? null" icon="mdi:alert-circle-outline" />
      </TKpiRow>
    </template>

    <!-- ─── Modules ─── -->
    <template #modules>
      <div class="t-table-tabs__toolbar">
        <NInput
          v-model:value="moduleFilter"
          :placeholder="t('filter.module')"
          clearable size="small"
          class="!w-280px max-w-full"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
        <NButton size="small" @click="refreshModules">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
      <TResponsiveTable
        :columns="moduleColumns"
        :data="filteredModules"
        :loading="modulesLoading"
        :pagination="modulesPagination"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>

    <!-- ─── Controllers ─── -->
    <template #controllers>
      <div class="t-table-tabs__toolbar">
        <NInput
          v-model:value="controllerFilter"
          :placeholder="t('filter.controller')"
          clearable size="small"
          class="!w-280px max-w-full"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
        <NSelect
          v-model:value="moduleFilterForCtrls"
          :options="moduleSelectOptions"
          :placeholder="t('filter.byModule')"
          clearable
          size="small"
          class="!w-200px max-w-full"
        />
        <NButton size="small" @click="refreshControllers">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
      <TResponsiveTable
        :columns="controllerColumns"
        :data="filteredControllers"
        :loading="controllersLoading"
        :pagination="controllersPagination"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>

    <!-- ─── Exceptions ─── -->
    <template #exceptions>
      <div class="t-table-tabs__toolbar">
        <NSelect
          v-model:value="windowMinutes"
          :options="windowOptions"
          size="small"
          class="!w-160px max-w-full"
          @update:value="refreshExceptions"
        />
        <NButton size="small" @click="refreshExceptions">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
        <NPopconfirm v-if="can('system.diagnostics.execute')" @positive-click="clearExceptions">
          <template #trigger>
            <NButton size="small" type="warning" tertiary>
              <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
              {{ t('actions.clear') }}
            </NButton>
          </template>
          {{ t('clearConfirm') }}
        </NPopconfirm>
      </div>

      <div v-if="summary && (summary.topExceptions?.length || statusCodeRows.length || errorCodeRows.length)" class="t-diagnostics-page__bd">
        <NCard size="small" :title="t('top.byType')" :bordered="false" class="t-tab-card">
          <NList v-if="summary.topExceptions?.length" hoverable>
            <NListItem v-for="row in summary.topExceptions" :key="row.exceptionType">
              <NThing>
                <template #header>
                  <code class="t-diagnostics-page__excname">{{ row.exceptionType }}</code>
                </template>
                <template #header-extra>
                  <NTag :bordered="false" type="error">{{ row.count }}</NTag>
                </template>
              </NThing>
            </NListItem>
          </NList>
          <NEmpty v-else size="small" :description="t('empty.exceptions')" />
        </NCard>
        <NCard size="small" :title="t('top.byStatusCode')" :bordered="false" class="t-tab-card">
          <NList v-if="statusCodeRows.length" hoverable>
            <NListItem v-for="row in statusCodeRows" :key="row.key">
              <NThing>
                <template #header>
                  <code class="t-diagnostics-page__excname">{{ row.key }}</code>
                </template>
                <template #header-extra>
                  <NTag :bordered="false" type="warning">{{ row.count }}</NTag>
                </template>
              </NThing>
            </NListItem>
          </NList>
          <NEmpty v-else size="small" :description="t('empty.exceptions')" />
        </NCard>
        <NCard size="small" :title="t('top.byErrorCode')" :bordered="false" class="t-tab-card">
          <NList v-if="errorCodeRows.length" hoverable>
            <NListItem v-for="row in errorCodeRows" :key="row.key">
              <NThing>
                <template #header>
                  <code class="t-diagnostics-page__excname">{{ row.key }}</code>
                </template>
                <template #header-extra>
                  <NTag :bordered="false" type="info">{{ row.count }}</NTag>
                </template>
              </NThing>
            </NListItem>
          </NList>
          <NEmpty v-else size="small" :description="t('empty.exceptions')" />
        </NCard>
      </div>

      <!-- No flex-height: the scroll pane owns the vertical scroll, so the
           table flows at natural height below the breakdown cards. -->
      <TResponsiveTable
        :columns="exceptionColumns"
        :data="exceptions"
        :loading="exceptionsLoading"
        :pagination="exceptionsPagination"
        :bordered="false"
        size="small"
      />
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, onMounted, ref } from 'vue'
import TResponsiveTable, { type TResponsivePagination } from '../../components/data/TResponsiveTable.vue'
import { TKpiCard, TKpiRow } from '../../components/data'
import {
  NButton,
  NCard,
  NEmpty,
  NInput,
  NList,
  NListItem,
  NPopconfirm,
  NSelect,
  NTag,
  NThing,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createDiagnosticsBridge,
  type ControllerDiagnosticsResultDto,
  type ControllerInfoDto,
  type ExceptionEntryDto,
  type ExceptionSummaryDto,
  type ModuleDiagnosticsDto,
} from '../../services/bridges/diagnostics-bridge'
import { makePageTranslator } from '../_shared/translate'
import { methodTone } from '../_shared/http-method'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import { usePermissionGuard } from '../../headless/usePermissionGuard'

const bridge = createDiagnosticsBridge({ client: useAdminClient() })

const t = makePageTranslator('system.diagnostics')

// 危险触发操作按钮级权限门控(fail-open,后端 [ApiAuthorize] 是真墙)
const { can } = usePermissionGuard()

// Deep-linkable primary tabs (?section=) - TTabsPage owns the deep-linking +
// Back/Forward; the page just declares the tabs.
const tabs: TabSection[] = [
  { name: 'modules', label: t('tabs.modules') },
  { name: 'controllers', label: t('tabs.controllers') },
  // Exceptions mixes a breakdown card grid + a table, so the pane owns the
  // vertical scroll (the table flows at natural height, not flex-height).
  { name: 'exceptions', label: t('tabs.exceptions'), scroll: true },
]

/**
 * Client-side pagination config shared by the three tabs: a "Total N" prefix
 * (mirrors TListShell's pager) + a page-size picker. `defaultPageSize` (not
 * `pageSize`) keeps the size uncontrolled so the picker actually works.
 */
function makeClientPagination(defaultPageSize: number): TResponsivePagination {
  return {
    defaultPageSize,
    showSizePicker: true,
    pageSizes: [...new Set([10, defaultPageSize, 50, 100])].sort((a, b) => a - b),
    prefix: ({ itemCount }: { itemCount?: number }) => `${t('admin.crud.total')} ${itemCount ?? 0}`,
  }
}
const modulesPagination = makeClientPagination(20)
const controllersPagination = makeClientPagination(25)
const exceptionsPagination = makeClientPagination(20)

// ─── Modules tab ───────────────────────────────────────────────────
const modules = ref<ModuleDiagnosticsDto[]>([])
const modulesLoading = ref(false)
const moduleFilter = ref('')

const moduleColumns: DataTableColumns<ModuleDiagnosticsDto> = [
  {
    title: () => t('cols.assembly'),
    key: 'assembly',
    width: 220,
    render: (row) =>
      h('div', { class: 'flex items-center gap-8px' }, [
        h(TSvgIcon, {
          icon: row.isEnabled ? 'mdi:check-circle' : 'mdi:circle-off-outline',
          size: 14,
          color: row.isEnabled ? 'var(--tnzi-success)' : 'var(--tnzi-base-text-muted)',
        }),
        h('span', { class: 'tnzi-mono' }, row.assembly),
      ]),
  },
  { title: () => t('cols.type'), key: 'type', width: 200, ellipsis: true },
  {
    title: () => t('cols.state'),
    key: 'initializationState',
    width: 130,
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.initializationState === 'Initialized' ? 'success' : 'default',
        },
        () => row.initializationState,
      ),
  },
  {
    title: () => t('cols.deps'),
    key: 'dependencyCount',
    width: 80,
    align: 'center',
  },
  {
    title: () => t('cols.services'),
    key: 'manifest.serviceCount',
    width: 100,
    align: 'center',
    render: (row) => row.manifest.serviceCount,
  },
  {
    title: () => t('cols.controllers'),
    key: 'manifest.controllers',
    width: 110,
    align: 'center',
    render: (row) => row.manifest.controllers.length,
  },
  {
    title: () => t('cols.events'),
    key: 'manifest.events',
    width: 90,
    align: 'center',
    render: (row) => row.manifest.events.length,
  },
  {
    title: () => t('cols.options'),
    key: 'manifest.options',
    width: 90,
    align: 'center',
    render: (row) => row.manifest.options.length,
  },
]

const filteredModules = computed(() => {
  const q = moduleFilter.value.trim().toLowerCase()
  if (!q) return modules.value
  return modules.value.filter(
    (m) => m.assembly.toLowerCase().includes(q) || m.type.toLowerCase().includes(q),
  )
})

async function refreshModules(): Promise<void> {
  modulesLoading.value = true
  try {
    modules.value = await bridge.modules.list()
  } catch {
    modules.value = []
  } finally {
    modulesLoading.value = false
  }
}

// ─── Controllers tab ───────────────────────────────────────────────
const controllerResult = ref<ControllerDiagnosticsResultDto | null>(null)
const controllersLoading = ref(false)
const controllerFilter = ref('')
const moduleFilterForCtrls = ref<string | null>(null)

const moduleSelectOptions = computed(() => {
  const all = controllerResult.value?.controllers.map((c) => c.module) ?? []
  return Array.from(new Set(all)).sort().map((m) => ({ label: m, value: m }))
})

const controllerColumns: DataTableColumns<ControllerInfoDto> = [
  {
    title: () => t('cols.route'),
    key: 'route',
    width: 280,
    render: (row) =>
      h('code', { class: 'tnzi-mono text-12px' }, row.route || EMPTY_DASH),
  },
  { title: () => t('cols.type'), key: 'type', ellipsis: { tooltip: true } },
  { title: () => t('cols.module'), key: 'module', width: 180 },
  {
    title: () => t('cols.methods'),
    key: 'methods',
    width: 240,
    render: (row) =>
      h(
        'div',
        { class: 'flex flex-wrap gap-4px' },
        (row.methods ?? []).map((m) =>
          h(NTag, { size: 'tiny', bordered: false, type: methodTone(m) }, () => m),
        ),
      ),
  },
  {
    title: () => t('cols.flags'),
    key: 'isDefault',
    width: 100,
    render: (row) =>
      row.isDefault
        ? h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => 'Default')
        : null,
  },
]

const filteredControllers = computed(() => {
  const all = controllerResult.value?.controllers ?? []
  const q = controllerFilter.value.trim().toLowerCase()
  const m = moduleFilterForCtrls.value
  return all.filter((c) => {
    if (m && c.module !== m) return false
    if (!q) return true
    return c.route.toLowerCase().includes(q) || c.type.toLowerCase().includes(q)
  })
})

async function refreshControllers(): Promise<void> {
  controllersLoading.value = true
  try {
    controllerResult.value = await bridge.controllers.list()
  } catch {
    controllerResult.value = null
  } finally {
    controllersLoading.value = false
  }
}

// ─── Exceptions tab ────────────────────────────────────────────────
const summary = ref<ExceptionSummaryDto | null>(null)
const exceptions = ref<ExceptionEntryDto[]>([])
const exceptionsLoading = ref(false)
const windowMinutes = ref<number>(60)
const windowOptions = [
  { value: 15, label: '15 min' },
  { value: 60, label: '1 h' },
  { value: 360, label: '6 h' },
  { value: 1440, label: '24 h' },
]

/** Map an HTTP status code to a Naive UI tag tone (2xx info, 4xx warning, 5xx error). */
function statusTone(code: number): 'info' | 'warning' | 'error' | 'default' {
  if (code >= 500) return 'error'
  if (code >= 400) return 'warning'
  if (code >= 200 && code < 300) return 'info'
  return 'default'
}

/** Flatten a `Record<string, number>` breakdown into sorted (desc) rows for list rendering. */
function toCountRows(record?: Record<string, number>): Array<{ key: string; count: number }> {
  if (!record) return []
  return Object.entries(record)
    .map(([key, count]) => ({ key, count }))
    .sort((a, b) => b.count - a.count)
}

const statusCodeRows = computed(() => toCountRows(summary.value?.byStatusCode))
const errorCodeRows = computed(() => toCountRows(summary.value?.byErrorCode))

const exceptionColumns: DataTableColumns<ExceptionEntryDto> = [
  {
    title: () => t('cols.occurredAt'),
    key: 'timestamp',
    width: 180,
    render: (row) => formatDate(row.timestamp),
  },
  {
    title: () => t('cols.exceptionType'),
    key: 'exceptionType',
    width: 220,
    render: (row) =>
      h('code', { class: 'tnzi-mono text-12px' }, row.exceptionType),
  },
  {
    title: () => t('cols.statusCode'),
    key: 'statusCode',
    width: 110,
    align: 'center',
    render: (row) =>
      row.statusCode != null
        ? h(NTag, { size: 'tiny', bordered: false, type: statusTone(row.statusCode) }, () => String(row.statusCode))
        : EMPTY_DASH,
  },
  {
    title: () => t('cols.errorCode'),
    key: 'errorCode',
    width: 160,
    render: (row) =>
      h('code', { class: 'tnzi-mono text-12px' }, row.errorCode || EMPTY_DASH),
  },
  {
    title: () => t('cols.requestId'),
    key: 'requestId',
    width: 200,
    render: (row) =>
      h('code', { class: 'tnzi-mono text-12px' }, row.requestId || EMPTY_DASH),
  },
  { title: () => t('cols.message'), key: 'message', ellipsis: { tooltip: true } },
]

async function refreshExceptions(): Promise<void> {
  exceptionsLoading.value = true
  try {
    const [s, recent] = await Promise.all([
      bridge.exceptions.getSummary(windowMinutes.value),
      bridge.exceptions.getRecent(50),
    ])
    summary.value = s
    exceptions.value = recent
  } catch {
    summary.value = null
    exceptions.value = []
  } finally {
    exceptionsLoading.value = false
  }
}

async function clearExceptions(): Promise<void> {
  try {
    await bridge.exceptions.clear()
    await refreshExceptions()
  } catch {
    // Bridge already swallows; refresh still safe.
  }
}

onMounted(() => {
  void refreshModules()
  void refreshControllers()
  void refreshExceptions()
})
</script>

<style scoped>
.t-diagnostics-page__bd {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 12px;
}
@media (max-width: 768px) {
  .t-diagnostics-page__bd {
    grid-template-columns: 1fr;
  }
}
.t-diagnostics-page__excname {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
}
</style>
