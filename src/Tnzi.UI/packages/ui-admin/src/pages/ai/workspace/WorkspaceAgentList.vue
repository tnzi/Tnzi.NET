<template>
  <!--
    WorkspaceAgentList — read-only list of Agent definitions discovered from
    AGENT.md files (~/.tnzi/agents/ globally or the project's .agents/
    directory). Sister page to /ai/agents (the editable DB table).

    Layout: vertical flex column with a header strip (title + filters +
    refresh) and a list card whose content fills the remaining viewport
    height. Mirrors TCrudPage proportions so the page reads as a peer of
    the standard CRUD pages even though it doesn't use TCrudPage itself
    (no pagination, no create/edit/delete).
  -->
  <div class="t-workspace-agent-page">
    <NCard
      :bordered="false"
      size="small"
      class="t-workspace-agent-page__card"
      content-style="padding: 0 16px 16px; display: flex; flex-direction: column; min-height: 0;"
    >
      <template #header>
        <div class="t-workspace-agent-page__header">
          <span class="t-workspace-agent-page__title">{{ t('title') }}</span>
          <NPopover trigger="hover" placement="bottom-start" :width="360">
            <template #trigger>
              <button type="button" class="t-workspace-agent-page__help" aria-label="info">
                <TSvgIcon icon="mdi:information-outline" :size="16" />
              </button>
            </template>
            <div class="t-workspace-agent-page__help-popover">{{ t('banner') }}</div>
          </NPopover>
        </div>
      </template>
      <template #header-extra>
        <div class="t-workspace-agent-page__toolbar">
          <NRadioGroup
            :value="scopeFilter"
            size="small"
            @update:value="onScopeChange"
          >
            <NRadioButton value="all">{{ t('scope.all') }}</NRadioButton>
            <NRadioButton value="Global">{{ t('scope.global') }}</NRadioButton>
            <NRadioButton value="Project">{{ t('scope.project') }}</NRadioButton>
          </NRadioGroup>
          <NButton size="small" :loading="loading" @click="reload">
            <template #icon>
              <TSvgIcon icon="mdi:refresh" :size="14" />
            </template>
            {{ t('actions.refresh') }}
          </NButton>
        </div>
      </template>

      <NDataTable
        class="t-workspace-agent-page__table"
        :columns="(columns as never)"
        :data="dataTableRows"
        :loading="loading"
        :row-key="rowKey"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </NCard>

    <!-- Detail drawer — read-only view of AGENT.md + PERSONA.md content. -->
    <NDrawer
      v-model:show="detailVisible"
      :width="640"
      placement="right"
    >
      <NDrawerContent v-if="detail" :title="detail.name" closable>
        <div class="t-workspace-agent-page__detail-meta">
          <div><strong>{{ t('detail.agentId') }}:</strong> <code>{{ detail.agentId }}</code></div>
          <div><strong>{{ t('detail.scope') }}:</strong> {{ detail.workspaceScope }}</div>
          <div><strong>{{ t('detail.filePath') }}:</strong> <code>{{ detail.filePath }}</code></div>
          <div v-if="detail.provider">
            <strong>{{ t('detail.provider') }}:</strong>
            {{ detail.provider }}{{ detail.model ? ` · ${detail.model}` : '' }}
          </div>
          <div v-if="detail.executionMode">
            <strong>{{ t('detail.executionMode') }}:</strong> {{ detail.executionMode }}
          </div>
          <div v-if="(detail.toolGroups?.length ?? 0) > 0">
            <strong>{{ t('detail.toolGroups') }}:</strong>
            <NTag
              v-for="g in (detail.toolGroups ?? [])"
              :key="g"
              size="small"
              style="margin-left: 4px"
            >{{ g }}</NTag>
          </div>
          <div v-if="detail.description">
            <strong>{{ t('detail.description') }}:</strong> {{ detail.description }}
          </div>
        </div>

        <div class="t-workspace-agent-page__detail-section">
          <h3>{{ t('detail.instructions') }}</h3>
          <pre class="t-workspace-agent-page__detail-body">{{ detail.instructions || t('detail.noInstructions') }}</pre>
        </div>

        <div v-if="detail.personaContent" class="t-workspace-agent-page__detail-section">
          <h3>{{ t('detail.persona') }}</h3>
          <pre class="t-workspace-agent-page__detail-body">{{ detail.personaContent }}</pre>
        </div>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NDrawer,
  NDrawerContent,
  NPopover,
  NRadioButton,
  NRadioGroup,
  NTag,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../../plugin/client'
// 0.2.72+ (B4): routed through ai-bridge so the page no longer needs to
// import `useAdminWorkspaceAgentApi` directly. The bridge wraps the
// admin API and exposes a minimal `list()` for this read-only view.
import { createWorkspaceAgentsBridge } from '../../../services/bridges/ai-bridge'
import type { WorkspaceAgentDto } from '@tnzi/core/services/ai'
import { translatePageKey } from '../../_shared/translate'

const t = (key: string) => translatePageKey('ai.workspaceAgents', key)
const message = (() => {
  try { return useMessage() } catch { return null }
})()

const workspaceAgents = createWorkspaceAgentsBridge({ client: useAdminClient() })
const agents = ref<WorkspaceAgentDto[]>([])
const loading = ref(false)
const scopeFilter = ref<'all' | 'Global' | 'Project'>('all')

function onScopeChange(value: 'all' | 'Global' | 'Project'): void {
  scopeFilter.value = value
}

const filteredAgents = computed(() => {
  if (scopeFilter.value === 'all') return agents.value
  return agents.value.filter((a) => a.workspaceScope === scopeFilter.value)
})

const dataTableRows = computed(() => filteredAgents.value as unknown as Record<string, unknown>[])
function rowKey(row: Record<string, unknown>): string {
  return String((row as { agentId?: string }).agentId ?? '')
}

async function reload(): Promise<void> {
  loading.value = true
  try {
    agents.value = await workspaceAgents.list()
  } catch (e) {
    agents.value = []
    message?.error(e instanceof Error ? e.message : t('loadError'))
  } finally {
    loading.value = false
  }
}

// Columns: typed loosely as `Record<string, unknown>[]` because the
// page-config layer doesn't depend on naive-ui's internal TableColumn
// type. The `as never` cast on the prop binding hands the array to
// NDataTable verbatim — render functions / titles / widths all flow
// through unchanged.
const columns = computed<Record<string, unknown>[]>(() => [
  {
    title: t('columns.name'),
    key: 'name',
    width: 220,
    fixed: 'left',
    render: (row: WorkspaceAgentDto) => h('div', [
      h('div', { style: 'font-weight: 500; line-height: 1.4' }, row.name),
      h('div', { style: 'font-size: 11px; color: var(--tnzi-base-text-muted); line-height: 1.3' }, row.agentId),
    ]),
  },
  {
    title: t('columns.scope'),
    key: 'workspaceScope',
    width: 100,
    render: (row: WorkspaceAgentDto) =>
      h(NTag, {
        size: 'small',
        type: row.workspaceScope === 'Project' ? 'warning' : 'info',
        bordered: false,
      }, () => row.workspaceScope === 'Project' ? t('scope.project') : t('scope.global')),
  },
  {
    title: t('columns.description'),
    key: 'description',
    ellipsis: { tooltip: true },
    render: (row: WorkspaceAgentDto) => row.description ?? '—',
  },
  {
    title: t('columns.provider'),
    key: 'provider',
    width: 180,
    render: (row: WorkspaceAgentDto) => {
      if (!row.provider) return '—'
      return row.model ? `${row.provider} · ${row.model}` : row.provider
    },
  },
  {
    title: t('columns.tags'),
    key: 'tags',
    width: 220,
    render: (row: WorkspaceAgentDto) => {
      const tags = [
        ...(row.domains ?? []).map((d) => h(NTag, { size: 'small', type: 'info', bordered: false }, () => d)),
        ...(row.roles ?? []).map((r) => h(NTag, { size: 'small', type: 'success', bordered: false }, () => r)),
      ]
      return tags.length === 0
        ? h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—')
        : h('div', { style: 'display: flex; gap: 4px; flex-wrap: wrap' }, tags)
    },
  },
  {
    title: t('columns.persona'),
    key: 'hasPersona',
    width: 90,
    align: 'center',
    render: (row: WorkspaceAgentDto) =>
      row.hasPersona
        ? h(NTag, { size: 'small', type: 'success', bordered: false }, () => t('persona.yes'))
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    title: t('columns.actions'),
    key: 'actions',
    width: 170,
    align: 'center',
    fixed: 'right',
    render: (row: WorkspaceAgentDto) =>
      h('div', { style: 'display: inline-flex; gap: 6px;' }, [
        h(NButton, {
          size: 'small',
          type: 'primary',
          ghost: true,
          onClick: () => openDetail(row),
        }, () => t('actions.view')),
        h(NButton, {
          size: 'small',
          ghost: true,
          onClick: () => copyPath(row.filePath),
        }, () => t('actions.copyPath')),
      ]),
  },
])

async function copyPath(path: string): Promise<void> {
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      await navigator.clipboard.writeText(path)
      message?.success(t('actions.copied'))
    }
  } catch {
    // ignore — clipboard access may be denied in some contexts
  }
}

const detail = ref<WorkspaceAgentDto | null>(null)
const detailVisible = ref(false)
function openDetail(row: WorkspaceAgentDto): void {
  detail.value = row
  detailVisible.value = true
}

onMounted(() => void reload())
</script>

<style scoped>
/* Vertical flex column anchored to the page wrapper — matches TCrudPage's
   layout proportions so the list card claims all remaining viewport
   height and the NDataTable inside can use flex-height. */
.t-workspace-agent-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}
.t-workspace-agent-page__card {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
/* NCard's `n-card__content` becomes the flex container the inline
   `content-style` configured above. The table fills the rest. */
.t-workspace-agent-page__card :deep(.n-card__content) {
  flex: 1 1 auto;
  min-height: 0;
}
.t-workspace-agent-page__table {
  flex: 1 1 auto;
  min-height: 0;
}
.t-workspace-agent-page__header {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-workspace-agent-page__title {
  font-size: 16px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-workspace-agent-page__help {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  border: none;
  background: transparent;
  color: var(--tnzi-base-text-muted, #888);
  cursor: pointer;
  padding: 0;
  margin-left: 4px;
  transition: color 0.15s ease, background 0.15s ease;
}
.t-workspace-agent-page__help:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08);
  color: var(--tnzi-primary);
}
.t-workspace-agent-page__help-popover {
  font-size: 13px;
  line-height: 1.5;
}
.t-workspace-agent-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-workspace-agent-page__detail-meta {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.t-workspace-agent-page__detail-section {
  margin-top: 16px;
}
.t-workspace-agent-page__detail-section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-workspace-agent-page__detail-body {
  margin: 0;
  padding: 12px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  border-radius: 6px;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 60vh;
  overflow: auto;
}
/* Table cell rhythm matches TCrudPage so visual scan reads the same as
   peer pages (soybean-style 9/12 vertical/horizontal padding). */
.t-workspace-agent-page__card :deep(.n-data-table-th) {
  padding: 8px 12px;
}
.t-workspace-agent-page__card :deep(.n-data-table-td) {
  padding: 9px 12px;
}
</style>
