<template>
  <!--
    McpServers - production-grade three-tab MCP control surface.

      Tab 1 «External Servers» - entity-driven CRUD over AI_McpServerRegistration:
        the external MCP servers Tnzi connects to as a CLIENT. A responsive card
        grid (TCardPage) with per-card Test Connection, full create/edit modal
        (authToken tri-state) + delete confirm.

      Tab 2 «This Server» - read-only status of Tnzi's OWN self-hosted MCP server
        (Tnzi as the server, exposing its tools to external MCP clients). KPI strip
        + descriptions, "Exposed Agents" management (expose/remove), and the
        registered-tools list.

      Tab 3 «Tool Analytics» - MCP tool-call telemetry: most-used ranking table;
        clicking a row opens a drawer with per-tool stats (p95, error rate, unique
        callers, first/last used) + recent error breakdown. A Cleanup popconfirm
        prunes old usage records.
  -->
  <TTabsPage
    :title="t('title')"
    icon="mdi:transit-connection-variant"
    :help="t('banner')"
    :translate="t"
    :sections="sections"
    default-section="servers"
    v-model:section="activeTab"
  >
    <!-- Keyword search lives in the single page-header bar (C2). It only
         applies to the External Servers tab, so it hides on the others. -->
    <template #actions="{ active }">
      <div v-if="active === 'servers'" class="t-mcp__search">
        <NInput
          v-model:value="serverSearch"
          size="small"
          clearable
          :placeholder="t('servers.searchPlaceholder')"
          class="t-mcp__search-input"
          @keydown.enter="onServerSearch"
          @clear="onServerSearchClear"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="16" /></template>
        </NInput>
        <NButton size="small" type="primary" @click="onServerSearch">
          {{ t('admin.crud.search') }}
        </NButton>
      </div>
    </template>

    <!-- ─── Tab 1: External Servers ─────────────────────────────── -->
    <template #servers>
            <!-- show-header=false suppresses the shell's own header bar:
                 without it the shell falls back to the route meta title and
                 renders a SECOND "MCP" bar. The page title + keyword search
                 live on the outer TContentPage header instead. -->
            <TCardPage
              :state="serverCrud"
              mode="page"
              :cols="{ xs: 1, sm: 2, md: 3, lg: 3 }"
              :form-modal-width="800"
              :show-header="false"
              :translate="t"
            >
              <template #card="{ item }">
                <TEntityCard>
                  <div class="flex items-center gap-8px mb-6px">
                    <span class="t-mcp-server-card__icon">
                      <TSvgIcon :icon="mcpTransportIcon(item.transport)" :size="18" />
                    </span>
                    <span class="t-mcp-server-card__name flex-1">{{ item.name }}</span>
                    <NTag
                      size="small"
                      :type="item.isEnabled ? 'success' : 'warning'"
                      :bordered="false"
                    >
                      {{ item.isEnabled ? t('status.enabled') : t('status.disabled') }}
                    </NTag>
                  </div>
                  <div class="t-mcp-server-card__url font-mono">
                    {{ item.serverUrl }}
                  </div>
                  <div class="t-mcp-server-card__meta">
                    <span>{{ t('servers.transport') }}: {{ item.transport }}</span>
                    <span>·</span>
                    <span>{{ t('servers.priority') }}: {{ item.priority }}</span>
                    <span v-if="item.hasAuthToken">·</span>
                    <NTag v-if="item.hasAuthToken" size="tiny" :bordered="false" type="info">
                      {{ t('servers.authed') }}
                    </NTag>
                  </div>
                  <template #actions>
                    <NTag
                      v-if="testState[item.id]?.result"
                      size="small"
                      :bordered="false"
                      class="mr-auto"
                      :type="testState[item.id]?.result?.ok ? 'success' : 'error'"
                    >
                      {{ testState[item.id]?.result?.message }}
                    </NTag>
                    <NButton
                      v-if="can('ai.mcp.execute')"
                      size="small"
                      ghost
                      :loading="testState[item.id]?.busy"
                      @click="onTestConnection(item.id)"
                    >
                      <template #icon><TSvgIcon icon="mdi:connection" :size="14" /></template>
                      {{ t('servers.test') }}
                    </NButton>
                    <NButton v-if="serverCrud.canUpdate" size="small" ghost @click="serverCrud.openEdit(item)">
                      {{ t('actions.edit') }}
                    </NButton>
                    <NPopconfirm v-if="serverCrud.canDelete" @positive-click="removeServer(item)">
                      <template #trigger>
                        <NButton size="small" type="error" ghost>{{ t('actions.delete') }}</NButton>
                      </template>
                      {{ t('servers.deleteConfirm') }}
                    </NPopconfirm>
                  </template>
                </TEntityCard>
              </template>

              <template #form="{ formData, mode }">
                <TFormSchemaRenderer
                  :schema="mcpServerFormSchema"
                  :model="(formData ?? {}) as Record<string, unknown>"
                  :readonly="mode === 'view'"
                  :translate="t"
                  :columns="2"
                />
                <p v-if="mode === 'edit'" class="t-mcp__form-hint">
                  {{ t('servers.tokenHint') }}
                </p>
              </template>
            </TCardPage>
    </template>

    <!-- ─── Tab 2: This Server (scroll: mixed variable-height content) ─── -->
    <template #status>
            <NSpin :show="statusLoading">
              <!-- Tnzi.AI.Mcp not loaded: friendly notice instead of a
                   silently empty status surface. -->
              <NAlert
                v-if="showModuleNotLoaded"
                :title="t('status.notLoadedTitle')"
                type="info"
                :closable="false"
              >
                {{ t('status.notLoadedBody') }}
              </NAlert>

              <!-- A refused or failed read says so: an empty status card would
                   read as "a disabled server with no tools". -->
              <NAlert v-else-if="statusError" type="error" :closable="false" data-test="mcp-status-error">
                {{ statusError }}
              </NAlert>

              <template v-else>
              <NAlert
                v-if="status && !status.enabled"
                :title="t('status.disabledTitle')"
                type="warning"
                :closable="false"
                class="mb-12px"
              >
                {{ t('status.disabledBody') }}
              </NAlert>

              <TKpiRow cols="1 s:2 m:4">
                <TKpiCard :label="t('status.state')" :value="status?.enabled ? t('status.enabled') : t('status.disabled')" :tone="status?.enabled ? 'success' : 'default'" />
                <TKpiCard :label="t('status.totalTools')" :value="status?.totalToolCount ?? null" />
                <TKpiCard :label="t('status.customTools')" :value="status?.customToolCount ?? null" />
                <TKpiCard :label="t('status.exposedAgents')" :value="status?.exposedAgentCount ?? null" />
              </TKpiRow>

              <NCard size="small" :title="t('status.detailsTitle')" :bordered="false" class="mt-12px t-tab-card">
                <NDescriptions :column="2" label-placement="left" size="small">
                  <NDescriptionsItem :label="t('status.endpoint')">
                    <code class="font-mono">{{ status?.endpoint || EMPTY_DASH }}</code>
                  </NDescriptionsItem>
                  <NDescriptionsItem :label="t('status.requireAuth')">
                    <NTag size="tiny" :bordered="false" :type="status?.requireAuthentication ? 'success' : 'default'">
                      {{ status?.requireAuthentication ? t('status.yes') : t('status.no') }}
                    </NTag>
                  </NDescriptionsItem>
                  <NDescriptionsItem :label="t('status.rateLimitPerMinute')">
                    {{ status?.rateLimitPerMinute ?? 0 }}
                  </NDescriptionsItem>
                </NDescriptions>
              </NCard>

              <!-- Exposed Agents management -->
              <NCard size="small" :title="t('expose.title')" :bordered="false" class="mt-12px t-tab-card">
                <template #header-extra>
                  <div v-if="can('ai.mcpServer.update')" class="flex items-center gap-6px">
                    <NSelect
                      v-model:value="agentToExpose"
                      size="small"
                      class="w-220px"
                      filterable
                      clearable
                      :placeholder="t('expose.selectPlaceholder')"
                      :options="exposableAgentOptions"
                      :loading="agentsLoading"
                    />
                    <NButton
                      size="small"
                      type="primary"
                      :disabled="!agentToExpose"
                      :loading="exposeBusy"
                      @click="onExposeAgent"
                    >
                      <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
                      {{ t('expose.add') }}
                    </NButton>
                  </div>
                </template>
                <!-- The agent catalog could not be read: the picker is empty for
                     that reason, and exposed agents show by id. -->
                <NAlert v-if="agentsError" type="warning" :closable="false" class="mb-8px" data-test="mcp-agents-error">
                  {{ agentsError }}
                </NAlert>
                <div v-if="exposedAgents.length" class="flex flex-col gap-6px">
                  <div
                    v-for="row in exposedAgents"
                    :key="row.agentId"
                    class="t-mcp__exposed-row"
                  >
                    <span class="flex items-center gap-8px flex-1 min-w-0">
                      <TSvgIcon icon="mdi:robot-outline" :size="16" />
                      <span class="t-mcp__exposed-name">{{ row.name }}</span>
                      <code class="font-mono t-mcp__exposed-id">{{ row.agentId }}</code>
                    </span>
                    <NPopconfirm v-if="can('ai.mcpServer.update')" @positive-click="onRemoveAgent(row.agentId)">
                      <template #trigger>
                        <NButton size="small" type="error" tertiary>
                          <template #icon><TSvgIcon icon="mdi:close" :size="14" /></template>
                          {{ t('expose.remove') }}
                        </NButton>
                      </template>
                      {{ t('expose.removeConfirm') }}
                    </NPopconfirm>
                  </div>
                </div>
                <div v-else class="t-mcp__empty">{{ t('expose.none') }}</div>
                <div class="t-mcp__hint">{{ t('expose.runtimeOnly') }}</div>
              </NCard>

              <!-- Registered tools -->
              <NCard size="small" :title="t('tools.title')" :bordered="false" class="mt-12px t-tab-card">
                <div v-if="tools.length" class="flex flex-col gap-4px">
                  <div v-for="tool in tools" :key="tool.name" class="t-mcp__tool-row">
                    <code class="font-mono t-mcp__tool-name">{{ tool.name }}</code>
                    <span class="t-mcp__tool-desc">{{ tool.description || t('tools.noDescription') }}</span>
                  </div>
                </div>
                <div v-else class="t-mcp__empty">{{ t('tools.none') }}</div>
              </NCard>
              </template>
            </NSpin>
    </template>

    <!-- ─── Tab 3: Tool Analytics ───────────────────────────────── -->
    <template #analytics>
            <!-- Tnzi.AI.Mcp not loaded: friendly notice instead of an empty
                 analytics table. -->
            <NAlert
              v-if="showModuleNotLoaded"
              :title="t('status.notLoadedTitle')"
              type="info"
              :closable="false"
            >
              {{ t('status.notLoadedBody') }}
            </NAlert>
            <NAlert v-else-if="analyticsError" type="error" :closable="false" data-test="mcp-analytics-error">
              {{ analyticsError }}
            </NAlert>
            <template v-else>
            <div class="t-mcp__analytics-bar">
              <span class="t-mcp__analytics-hint">{{ t('analytics.hint') }}</span>
              <NPopconfirm v-if="can('ai.mcpServer.delete')" @positive-click="onCleanup">
                <template #trigger>
                  <NButton size="small" :loading="cleanupBusy">
                    <template #icon><TSvgIcon icon="mdi:broom" :size="14" /></template>
                    {{ t('analytics.cleanup') }}
                  </NButton>
                </template>
                {{ t('analytics.cleanupConfirm', { days: retentionDays }) }}
              </NPopconfirm>
            </div>
            <TResponsiveTable
              :columns="analyticsColumns"
              :data="popularTools"
              :loading="analyticsLoading"
              :pagination="{ pageSize: 20 }"
              :bordered="false"
              size="small"
              :flex-height="true"
              :row-props="analyticsRowProps"
            />
            </template>
    </template>

    <!-- Tab-independent overlay: per-tool analytics detail drawer, driven by the
         single detail engine (deep-linkable `?tool=view:<name>`). -->
    <template #overlays>
      <TDetailHost :state="toolDetail" :title="detailTool ?? ''" :width="560" :footer="false" :translate="t">
        <template #default>
          <NSpin v-if="detailTool" :show="detailLoading">
            <div v-if="toolStats" class="t-mcp__stats">
              <NDescriptions :column="2" label-placement="top" size="small">
                <NDescriptionsItem :label="t('analytics.totalCalls')">
                  {{ toolStats.totalCalls }}
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.uniqueCallers')">
                  {{ toolStats.uniqueCallers }}
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.avgDuration')">
                  {{ Math.round(toolStats.avgDurationMs) }} ms
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.p95Duration')">
                  {{ Math.round(toolStats.p95DurationMs) }} ms
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.errorRate')">
                  <NTag size="tiny" :bordered="false" :type="toolStats.errorRate > 0 ? 'error' : 'success'">
                    {{ formatPercent(toolStats.errorRate) }}
                  </NTag>
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.firstUsed')">
                  {{ toolStats.firstUsed ? formatDateTime(toolStats.firstUsed) : EMPTY_DASH }}
                </NDescriptionsItem>
                <NDescriptionsItem :label="t('analytics.lastUsed')">
                  {{ toolStats.lastUsed ? formatDateTime(toolStats.lastUsed) : EMPTY_DASH }}
                </NDescriptionsItem>
              </NDescriptions>
            </div>

            <div class="t-mcp__stats-section">
              <h3>{{ t('analytics.errorsTitle') }}</h3>
              <div v-if="toolErrors.length" class="flex flex-col gap-6px">
                <div v-for="(err, idx) in toolErrors" :key="idx" class="t-mcp__error-row">
                  <div class="flex items-center gap-8px">
                    <NTag size="tiny" :bordered="false" type="error">{{ err.count }}×</NTag>
                    <span class="t-mcp__error-time">{{ formatDateTime(err.lastOccurred) }}</span>
                  </div>
                  <code class="font-mono t-mcp__error-msg">{{ err.errorMessage }}</code>
                </div>
              </div>
              <div v-else class="t-mcp__empty">{{ t('analytics.noErrors') }}</div>
            </div>
          </NSpin>
        </template>
      </TDetailHost>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NDescriptions,
  NDescriptionsItem,
  NInput,
  NPopconfirm,
  NSelect,
  NSpin,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { TKpiCard, TKpiRow } from '../../../components/data'
import { formatDateTime } from '@tnzi/core'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { useCrudPage } from '../../../headless/useCrudPage'
import { useDetail } from '../../../headless/useDetail'
import { fetchAllPages } from '../../../headless/fetchAllPages'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { useModuleAvailability } from '../../../headless/useModuleAvailability'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import {
  mcpServerFormSchema,
  mcpServerColumns,
  mcpTransportIcon,
  toCreateMcpServerDto,
  toUpdateMcpServerDto,
} from './mcp-server-config'
import type {
  McpServerRegistrationDto,
  McpServerStatusDto,
  McpToolInfoDto,
  McpToolPopularityDto,
  McpToolStatsDto,
  McpToolErrorDto,
  AgentDto,
} from '@tnzi/core/services/ai'

const t = makePageTranslator('ai.mcp')
const message = useSafeMessage()

const bridge = createAiBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()

// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward; the
// `status` pane holds mixed variable-height content so it owns its own scroll.
const sections: TabSection[] = [
  { name: 'servers', label: t('tabs.servers') },
  { name: 'status', label: t('tabs.status'), scroll: true },
  { name: 'analytics', label: t('tabs.analytics') },
]
const activeTab = ref('servers')

// ─── Tab 1: External servers CRUD ──────────────────────────────────────
const serverCrud = useCrudPage<McpServerRegistrationDto>({
  pageId: 'ai.mcp',
  permission: 'ai.mcp',
  columns: mcpServerColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.mcpServers.fetch(query),
  createData: (data) => bridge.mcpServers.create(toCreateMcpServerDto(data as Record<string, unknown>)),
  // The server's create defaults: an unseeded switch renders OFF while the
  // created record is enabled.
  createDefaults: { isEnabled: true, priority: 0 },
  updateData: (id, data) =>
    bridge.mcpServers.update(String(id), toUpdateMcpServerDto(data as Record<string, unknown>)),
  deleteData: (ids) => bridge.mcpServers.delete(ids.map(String)),
})

// Header keyword search for the External Servers tab. The TCardPage header
// bar is suppressed (single title bar on TContentPage), so the keyword search
// is wired manually to the same crud state the shell would drive.
const serverSearch = ref('')
function onServerSearch(): void {
  serverCrud.setSearch(serverSearch.value)
  void serverCrud.refresh()
}
function onServerSearchClear(): void {
  serverSearch.value = ''
  serverCrud.setSearch('')
  void serverCrud.refresh()
}

type TestEntry = { busy: boolean; result: { ok: boolean; message: string } | null }
const testState = reactive<Record<string, TestEntry>>({})

async function onTestConnection(id: string): Promise<void> {
  if (!id) return
  const existing = testState[id]
  if (existing?.busy) return
  testState[id] = { busy: true, result: null }
  try {
    const result = await bridge.mcpServers.test(id)
    testState[id] = {
      busy: false,
      result: result.ok
        ? { ok: true, message: t('servers.testOk', { ms: result.latency }) }
        : { ok: false, message: result.error || t('servers.testFailed') },
    }
  } catch (err) {
    testState[id] = {
      busy: false,
      result: { ok: false, message: err instanceof Error ? err.message : t('servers.testFailed') },
    }
  }
}

async function removeServer(item: McpServerRegistrationDto): Promise<void> {
  // handleDelete has already reported a refusal (toast or onError) before rethrowing.
  await serverCrud.handleDelete([item.id]).catch(() => undefined)
}

// ─── Server sub-module availability ────────────────────────────────────
// The External Servers tab is the AI core client registry and works on every
// host that loads Tnzi.AI. This Server + Tool Analytics are the optional
// Tnzi.AI.Mcp sub-module, so they ask the loaded-module signal before firing a
// request that can only 404. `has` is fail-open (no signal = old backend), so a
// 404 / 501 from the server endpoints also reads as "not loaded".
const MCP_SERVER_MODULE = 'ai-mcp'
const {
  has: hasModule,
  canActivate,
  pending: modulesPending,
  modules: loadedModules,
} = useModuleAvailability()
const serverAnsweredAbsent = ref(false)
const showModuleNotLoaded = computed(() => !hasModule(MCP_SERVER_MODULE) || serverAnsweredAbsent.value)

const MODULE_ABSENT_CODES = new Set([404, 501])

/** Records a failed server-tab read: "module absent" for 404 / 501, otherwise the reason. */
function serverReadFailure(error: unknown): string | null {
  const statusCode = (error as { statusCode?: number } | null)?.statusCode
  if (statusCode !== undefined && MODULE_ABSENT_CODES.has(statusCode)) {
    serverAnsweredAbsent.value = true
    return null
  }
  return error instanceof Error ? error.message : String(error)
}

// ─── Tab 2: This server status + exposed agents + tools ────────────────
const statusLoading = ref(false)
const status = ref<McpServerStatusDto | null>(null)
const statusError = ref<string | null>(null)
const tools = ref<McpToolInfoDto[]>([])
const exposedAgentIds = ref<string[]>([])
const allAgents = ref<AgentDto[]>([])
const agentsLoading = ref(false)
/** A refused agent-list read: the picker is empty because it could not be read, not because there are no agents. */
const agentsError = ref<string | null>(null)
const agentToExpose = ref<string | null>(null)
const exposeBusy = ref(false)

const agentNameById = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {}
  for (const a of allAgents.value) map[a.id] = a.name
  return map
})

const exposedAgents = computed<Array<{ agentId: string; name: string }>>(() =>
  exposedAgentIds.value.map((id) => ({ agentId: id, name: agentNameById.value[id] ?? id })),
)

const exposableAgentOptions = computed(() => {
  const exposed = new Set(exposedAgentIds.value)
  return allAgents.value
    .filter((a) => !exposed.has(a.id))
    .map((a) => ({ label: a.name, value: a.id }))
})

/** A refused write's reason, for the toast. */
function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

/**
 * The agent catalog the expose picker offers and exposed ids are named from.
 * Read on its own: it needs `ai.agent.view`, which a holder of the MCP server
 * codes may lack, and its refusal says nothing about the server itself.
 */
async function loadAgents(): Promise<void> {
  agentsLoading.value = true
  agentsError.value = null
  try {
    // Every agent, not the first clamped page: this list decides which agents can be exposed.
    allAgents.value = await fetchAllPages((q) => bridge.agents.fetch(q))
  } catch (error) {
    allAgents.value = []
    agentsError.value = reasonOf(error)
  } finally {
    agentsLoading.value = false
  }
}

async function loadStatusTab(): Promise<void> {
  if (!canActivate(MCP_SERVER_MODULE)) return
  statusLoading.value = true
  statusError.value = null
  const agentsLoad = loadAgents()
  try {
    const [st, tls, exposed] = await Promise.all([
      bridge.mcp.getStatus(),
      bridge.mcp.getTools(),
      bridge.mcp.getExposedAgents(),
    ])
    status.value = st
    tools.value = tls
    exposedAgentIds.value = exposed
  } catch (error) {
    status.value = null
    tools.value = []
    exposedAgentIds.value = []
    statusError.value = serverReadFailure(error)
  } finally {
    statusLoading.value = false
  }
  await agentsLoad
}

async function onExposeAgent(): Promise<void> {
  if (!agentToExpose.value || exposeBusy.value) return
  exposeBusy.value = true
  try {
    await bridge.mcp.exposeAgent(agentToExpose.value)
    exposedAgentIds.value = await bridge.mcp.getExposedAgents()
    agentToExpose.value = null
  } catch (error) {
    message.error(reasonOf(error))
  } finally {
    exposeBusy.value = false
  }
}

async function onRemoveAgent(agentId: string): Promise<void> {
  try {
    await bridge.mcp.removeAgent(agentId)
    exposedAgentIds.value = await bridge.mcp.getExposedAgents()
  } catch (error) {
    message.error(reasonOf(error))
  }
}

// ─── Tab 3: Tool analytics ─────────────────────────────────────────────
const analyticsLoading = ref(false)
const analyticsError = ref<string | null>(null)
const popularTools = ref<McpToolPopularityDto[]>([])
const retentionDays = 90
const cleanupBusy = ref(false)

function formatPercent(rate: number): string {
  return `${(rate * 100).toFixed(1)}%`
}

const analyticsColumns: DataTableColumns<McpToolPopularityDto> = [
  {
    title: () => t('analytics.toolName'),
    key: 'toolName',
    render: (row) => h('code', { class: 'font-mono text-12px' }, row.toolName),
  },
  {
    title: () => t('analytics.callCount'),
    key: 'callCount',
    width: 120,
    align: 'right',
    sorter: (a, b) => a.callCount - b.callCount,
    defaultSortOrder: 'descend',
  },
  {
    title: () => t('analytics.successRate'),
    key: 'successRate',
    width: 130,
    align: 'right',
    render: (row) =>
      h(
        NTag,
        { size: 'tiny', bordered: false, type: row.successRate >= 0.99 ? 'success' : row.successRate >= 0.9 ? 'warning' : 'error' },
        () => formatPercent(row.successRate),
      ),
  },
  {
    title: () => t('analytics.avgDuration'),
    key: 'avgDurationMs',
    width: 130,
    align: 'right',
    render: (row) => `${Math.round(row.avgDurationMs)} ms`,
  },
]

function analyticsRowProps(row: McpToolPopularityDto) {
  return {
    class: 't-mcp__analytics-row',
    onClick: () => void openToolDetail(row.toolName),
  }
}

async function loadAnalyticsTab(): Promise<void> {
  if (!canActivate(MCP_SERVER_MODULE)) return
  analyticsLoading.value = true
  analyticsError.value = null
  try {
    popularTools.value = await bridge.mcpToolAnalytics.getMostUsedTools(20)
  } catch (error) {
    popularTools.value = []
    analyticsError.value = serverReadFailure(error)
  } finally {
    analyticsLoading.value = false
  }
}

async function onCleanup(): Promise<void> {
  if (cleanupBusy.value) return
  cleanupBusy.value = true
  try {
    await bridge.mcpToolAnalytics.cleanup(retentionDays)
    await loadAnalyticsTab()
  } catch (error) {
    message.error(reasonOf(error))
  } finally {
    cleanupBusy.value = false
  }
}

// Per-tool analytics drawer - a record detail (the tool) driven by the single
// detail engine so it is deep-linkable (`?tool=view:<toolName>`),
// refresh-survivable and Back-closeable. `source` hydrates a cold-load deep
// link from the loaded popularity ranking; `getId` keys on the tool name.
// `:footer="false"` - a read-only panel; the X closes it.
const toolDetail = useDetail<McpToolPopularityDto>({
  mode: 'drawer',
  url: 'tool',
  getId: (row) => row.toolName,
  source: { items: popularTools, loading: analyticsLoading },
})
const detailTool = computed(() => toolDetail.data.value?.toolName ?? null)
const detailLoading = ref(false)
const toolStats = ref<McpToolStatsDto | null>(null)
const toolErrors = ref<McpToolErrorDto[]>([])

async function openToolDetail(toolName: string): Promise<void> {
  await toolDetail.open('view', toolName)
}

// Load per-tool stats + errors whenever the drawer (re)binds to a tool - covers
// an in-session open AND a `?tool=view:<name>` cold-load deep link / refresh.
watch(() => toolDetail.data.value, async (row) => {
  if (!row) return
  detailLoading.value = true
  toolStats.value = null
  toolErrors.value = []
  try {
    const [stats, errors] = await Promise.all([
      bridge.mcpToolAnalytics.getToolStats(row.toolName),
      bridge.mcpToolAnalytics.getToolErrors(row.toolName, 10),
    ])
    toolStats.value = stats
    toolErrors.value = errors
  } catch {
    toolStats.value = null
    toolErrors.value = []
  } finally {
    detailLoading.value = false
  }
})

// The server tabs load once the module signal says the sub-module is there. At
// mount the probe may still be in flight (`canActivate` false), so the watch
// picks the load up when it settles instead of leaving both tabs empty.
let serverTabsRequested = false
function loadServerTabs(): void {
  if (serverTabsRequested || !canActivate(MCP_SERVER_MODULE)) return
  serverTabsRequested = true
  void loadStatusTab()
  void loadAnalyticsTab()
}

onMounted(loadServerTabs)
watch(() => [modulesPending.value, loadedModules.value] as const, loadServerTabs)

defineExpose({
  activeTab,
  status,
  statusError,
  agentsError,
  analyticsError,
  showModuleNotLoaded,
  tools,
  agentToExpose,
  exposedAgents,
  popularTools,
  toolStats,
  onTestConnection,
  onExposeAgent,
  onRemoveAgent,
  openToolDetail,
  onCleanup,
  loadStatusTab,
  loadAnalyticsTab,
})
</script>

<style scoped>
/* Page-header keyword search (External Servers tab only). */
.t-mcp__search {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-mcp__search-input {
  width: 240px;
  max-width: 100%;
}
@media (max-width: 640px) {
  .t-mcp__search { flex-wrap: wrap; }
  .t-mcp__search-input { width: 100%; }
}
.t-mcp__form-hint {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-mcp__empty {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-mcp__hint {
  margin-top: 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
/* External-server card */
.t-mcp-server-card__icon {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.t-mcp-server-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-mcp-server-card__url {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-mcp-server-card__meta {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
/* Exposed agents */
.t-mcp__exposed-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 6px;
  background: var(--tnzi-bg-deep, #f6f8fa);
}
.t-mcp__exposed-name {
  font-size: 13px;
  color: var(--tnzi-base-text);
  flex-shrink: 0;
}
.t-mcp__exposed-id {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #aaa);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* Registered tools */
.t-mcp__tool-row {
  display: flex;
  align-items: baseline;
  gap: 12px;
  padding: 4px 0;
  border-bottom: 1px solid var(--tnzi-border, #eee);
}
.t-mcp__tool-name {
  font-size: 12px;
  color: var(--tnzi-primary, #6d5ce7);
  flex-shrink: 0;
  min-width: 160px;
}
.t-mcp__tool-desc {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.4;
}
/* Analytics tab */
.t-mcp__analytics-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}
.t-mcp__analytics-hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-mcp__analytics-row {
  cursor: pointer;
}
/* Tool detail drawer */
.t-mcp__stats-section {
  margin-top: 16px;
}
.t-mcp__stats-section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-mcp__error-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px;
  border-radius: 6px;
  background: var(--tnzi-bg-deep, #f6f8fa);
}
.t-mcp__error-time {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-mcp__error-msg {
  font-size: 11px;
  color: var(--tnzi-error, #d03050);
  word-break: break-word;
}
</style>
