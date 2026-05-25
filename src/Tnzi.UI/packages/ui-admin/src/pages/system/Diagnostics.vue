<template>
  <!--
    Diagnostics — surfaces `/admin/diagnostics/*` (ships with HostingModule).
    Three tabs:
      • Modules     — loaded ModuleManifest list (assembly, deps, services, events, options)
      • Controllers — every Mvc controller route the runtime exposes
      • Exceptions  — summary KPIs + recent ring buffer + clear

    Read-mostly. No edits, no per-row modal — this is an ops console, not a CRUD page.
  -->
  <div class="t-diagnostics-page t-stack-page">
    <div class="t-diagnostics-page__header">
      <div class="t-diagnostics-page__title">
        <TSvgIcon icon="mdi:stethoscope" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-diagnostics-page__kpis">
        <NStatistic :label="t('kpi.modules')" :value="modules.length">
          <template #suffix>
            <TSvgIcon icon="mdi:view-grid-outline" :size="14" />
          </template>
        </NStatistic>
        <NStatistic :label="t('kpi.controllers')" :value="controllerResult?.totalCount ?? 0">
          <template #suffix>
            <TSvgIcon icon="mdi:router-network" :size="14" />
          </template>
        </NStatistic>
        <NStatistic :label="t('kpi.exceptions', { minutes: summary?.windowMinutes ?? 60 })" :value="summary?.totalCount ?? 0">
          <template #suffix>
            <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
          </template>
        </NStatistic>
      </div>
    </div>

    <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
      <!-- ─── Modules ─── -->
      <NTabPane name="modules" :tab="t('tabs.modules')">
        <div class="t-table-tabs__pane">
          <div class="t-table-tabs__toolbar">
            <NInput
              v-model:value="moduleFilter"
              :placeholder="t('filter.module')"
              clearable
              style="width: 280px"
            >
              <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
            </NInput>
            <NButton size="small" @click="refreshModules">
              <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
              {{ t('actions.refresh') }}
            </NButton>
          </div>
          <NDataTable
            :columns="moduleColumns"
            :data="filteredModules"
            :loading="modulesLoading"
            :pagination="{ pageSize: 20 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>

      <!-- ─── Controllers ─── -->
      <NTabPane name="controllers" :tab="t('tabs.controllers')">
        <div class="t-table-tabs__pane">
          <div class="t-table-tabs__toolbar">
            <NInput
              v-model:value="controllerFilter"
              :placeholder="t('filter.controller')"
              clearable
              style="width: 280px"
            >
              <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
            </NInput>
            <NSelect
              v-model:value="moduleFilterForCtrls"
              :options="moduleSelectOptions"
              :placeholder="t('filter.byModule')"
              clearable
              size="medium"
              style="width: 200px"
            />
            <NButton size="small" @click="refreshControllers">
              <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
              {{ t('actions.refresh') }}
            </NButton>
          </div>
          <NDataTable
            :columns="controllerColumns"
            :data="filteredControllers"
            :loading="controllersLoading"
            :pagination="{ pageSize: 25 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>

      <!-- ─── Exceptions ─── -->
      <NTabPane name="exceptions" :tab="t('tabs.exceptions')">
        <div class="t-table-tabs__pane">
          <div class="t-table-tabs__toolbar">
            <NSelect
              v-model:value="windowMinutes"
              :options="windowOptions"
              size="medium"
              style="width: 160px"
              @update:value="refreshExceptions"
            />
            <NButton size="small" @click="refreshExceptions">
              <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
              {{ t('actions.refresh') }}
            </NButton>
            <NPopconfirm @positive-click="clearExceptions">
              <template #trigger>
                <NButton size="small" type="warning" tertiary>
                  <template #icon><TSvgIcon icon="mdi:delete-sweep-outline" :size="14" /></template>
                  {{ t('actions.clear') }}
                </NButton>
              </template>
              {{ t('clearConfirm') }}
            </NPopconfirm>
          </div>

          <div v-if="summary && (summary.topExceptions?.length || summary.byController?.length)" class="t-diagnostics-page__bd">
            <NCard size="small" :title="t('top.byType')" :bordered="false">
              <NList v-if="summary.topExceptions?.length" hoverable>
                <NListItem v-for="row in summary.topExceptions" :key="row.exceptionType">
                  <NThing>
                    <template #header>
                      <code class="t-diagnostics-page__excname">{{ row.exceptionType }}</code>
                    </template>
                    <template #description>
                      <span class="t-diagnostics-page__lastseen">{{ row.lastSeenUtc ? formatDate(row.lastSeenUtc) : '' }}</span>
                    </template>
                    <template #header-extra>
                      <NTag :bordered="false" type="error">{{ row.count }}</NTag>
                    </template>
                  </NThing>
                </NListItem>
              </NList>
              <NEmpty v-else size="small" :description="t('empty.exceptions')" />
            </NCard>
            <NCard size="small" :title="t('top.byController')" :bordered="false">
              <NList v-if="summary.byController?.length" hoverable>
                <NListItem v-for="row in summary.byController" :key="row.controller">
                  <NThing>
                    <template #header>
                      <span>{{ row.controller }}</span>
                    </template>
                    <template #header-extra>
                      <NTag :bordered="false" type="warning">{{ row.count }}</NTag>
                    </template>
                  </NThing>
                </NListItem>
              </NList>
              <NEmpty v-else size="small" :description="t('empty.exceptions')" />
            </NCard>
          </div>

          <NDataTable
            :columns="exceptionColumns"
            :data="exceptions"
            :loading="exceptionsLoading"
            :pagination="{ pageSize: 20 }"
            :bordered="false"
            size="small"
            :flex-height="true"
          />
        </div>
      </NTabPane>
    </NTabs>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NEmpty,
  NInput,
  NList,
  NListItem,
  NPopconfirm,
  NSelect,
  NStatistic,
  NTabPane,
  NTabs,
  NTag,
  NThing,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import {
  createDiagnosticsBridge,
  type ControllerDiagnosticsResultDto,
  type ControllerInfoDto,
  type ExceptionEntryDto,
  type ExceptionSummaryDto,
  type ModuleDiagnosticsDto,
} from '../../services/bridges/diagnostics-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'

const bridge = createDiagnosticsBridge({ client: useAdminClient() })

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('system.diagnostics', key), params)

const activeTab = ref<'modules' | 'controllers' | 'exceptions'>('modules')

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
      h('div', { style: 'display: flex; align-items: center; gap: 8px;' }, [
        h(TSvgIcon, {
          icon: row.isEnabled ? 'mdi:check-circle' : 'mdi:circle-off-outline',
          size: 14,
          style: row.isEnabled ? 'color: var(--tnzi-success)' : 'color: var(--tnzi-base-text-muted)',
        }),
        h('span', { style: 'font-family: var(--tnzi-font-mono)' }, row.assembly),
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
      h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.route || '—'),
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
        { style: 'display: flex; flex-wrap: wrap; gap: 4px;' },
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

const exceptionColumns: DataTableColumns<ExceptionEntryDto> = [
  {
    title: () => t('cols.occurredAt'),
    key: 'occurredAtUtc',
    width: 180,
    render: (row) => formatDate(row.occurredAtUtc),
  },
  {
    title: () => t('cols.exceptionType'),
    key: 'exceptionType',
    width: 220,
    render: (row) =>
      h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.exceptionType),
  },
  { title: () => t('cols.message'), key: 'message', ellipsis: { tooltip: true } },
  {
    title: () => t('cols.endpoint'),
    key: 'path',
    width: 220,
    render: (row) =>
      h('div', { style: 'display: flex; align-items: center; gap: 6px;' }, [
        row.method
          ? h(NTag, { size: 'tiny', bordered: false, type: methodTone(row.method) }, () => row.method ?? '')
          : null,
        h('span', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.path ?? '—'),
      ]),
  },
  {
    title: () => t('cols.duration'),
    key: 'durationMs',
    width: 100,
    align: 'right',
    render: (row) => (row.durationMs != null ? `${row.durationMs.toFixed(1)} ms` : '—'),
  },
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

// ─── Shared helpers ────────────────────────────────────────────────
function methodTone(m: string): 'success' | 'info' | 'warning' | 'error' | 'default' {
  switch ((m ?? '').toUpperCase()) {
    case 'GET': return 'info'
    case 'POST': return 'success'
    case 'PUT': return 'warning'
    case 'PATCH': return 'warning'
    case 'DELETE': return 'error'
    default: return 'default'
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  try {
    return new Date(iso).toLocaleString()
  } catch {
    return iso
  }
}

onMounted(() => {
  void refreshModules()
  void refreshControllers()
  void refreshExceptions()
})
</script>

<style scoped>
/* Layout shell comes from shared `.t-stack-page` + `.t-table-tabs`
   utilities in styles/polish.css. Only page-specific decorations live
   here. */
.t-diagnostics-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-diagnostics-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-diagnostics-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-diagnostics-page__kpis {
  display: flex;
  gap: 32px;
  flex-wrap: wrap;
}
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
.t-diagnostics-page__lastseen {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
