<template>
  <div class="t-workspace-agent-page">
    <TCardPage
      :state="crud"
      mode="page"
      :title="t('title')"
      :title-help="t('banner')"
      :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
      :show-search="false"
      :show-pagination="false"
      :translate="t"
    >
      <template #toolbarLeft>
        <NRadioGroup
          :value="scopeFilter"
          size="small"
          @update:value="onScopeChange"
        >
          <NRadioButton value="all">{{ t('scope.all') }}</NRadioButton>
          <NRadioButton value="Global">{{ t('scope.global') }}</NRadioButton>
          <NRadioButton value="Project">{{ t('scope.project') }}</NRadioButton>
        </NRadioGroup>
      </template>

      <template #card="{ item }">
        <NCard size="small" class="t-workspace-agent-page__agent-card">
          <div class="t-workspace-agent-page__card-header">
            <span class="t-workspace-agent-page__card-name">{{ item.name }}</span>
            <NTag
              size="small"
              :type="item.workspaceScope === 'Project' ? 'warning' : 'info'"
              :bordered="false"
            >
              {{ item.workspaceScope === 'Project' ? t('scope.project') : t('scope.global') }}
            </NTag>
          </div>
          <div class="t-workspace-agent-page__card-id">{{ item.agentId }}</div>
          <div class="t-workspace-agent-page__card-desc">
            {{ item.description || '—' }}
          </div>
          <div v-if="item.provider" class="t-workspace-agent-page__card-provider">
            {{ item.provider }}{{ item.model ? ` · ${item.model}` : '' }}
          </div>
          <div
            v-if="(item.domains?.length ?? 0) > 0 || (item.roles?.length ?? 0) > 0 || item.hasPersona"
            class="t-workspace-agent-page__card-tags"
          >
            <NTag
              v-for="d in (item.domains ?? [])"
              :key="`d-${d}`"
              size="small"
              type="info"
              :bordered="false"
            >{{ d }}</NTag>
            <NTag
              v-for="r in (item.roles ?? [])"
              :key="`r-${r}`"
              size="small"
              type="success"
              :bordered="false"
            >{{ r }}</NTag>
            <NTag
              v-if="item.hasPersona"
              size="small"
              type="success"
              :bordered="false"
            >{{ t('detail.persona') }}</NTag>
          </div>
          <div class="t-workspace-agent-page__card-actions">
            <NButton size="small" type="primary" ghost @click="openDetail(item)">
              {{ t('actions.view') }}
            </NButton>
            <NButton size="small" ghost @click="copyPath(item.filePath)">
              {{ t('actions.copyPath') }}
            </NButton>
          </div>
        </NCard>
      </template>
    </TCardPage>

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
              class="ml-4px"
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
import { ref } from 'vue'
import {
  NButton,
  NCard,
  NDrawer,
  NDrawerContent,
  NRadioButton,
  NRadioGroup,
  NTag,
  useMessage,
} from 'naive-ui'
import { useAdminClient } from '../../../plugin/client'
import { createWorkspaceAgentsBridge } from '../../../services/bridges/ai-bridge'
import type { WorkspaceAgentDto } from '@tnzi/core/services/ai'
import { translatePageKey } from '../../_shared/translate'
import { useCrudPage } from '../../../headless/useCrudPage'
import TCardPage from '../../../components/crud/TCardPage.vue'

const t = (key: string) => translatePageKey('ai.workspaceAgents', key)
const message = (() => {
  try { return useMessage() } catch { return null }
})()

const bridge = createWorkspaceAgentsBridge({ client: useAdminClient() })

const scopeFilter = ref<'all' | 'Global' | 'Project'>('all')

const crud = useCrudPage<WorkspaceAgentDto>({
  pageId: 'ai.workspace-agents',
  columns: [], // card page renders via #card slot; column defs unused (TCardPage has no column settings)
  rowKey: (r) => r.agentId,
  fetchData: async (q) => {
    const all = await bridge.list()
    const scope = q.filters.scope as string | undefined
    const items = scope && scope !== 'all'
      ? all.filter((a) => a.workspaceScope === scope)
      : all
    return {
      items,
      totalCount: items.length,
      pageIndex: 1,
      pageSize: items.length || 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    }
  },
})
crud.refresh().catch(() => undefined)

function onScopeChange(value: 'all' | 'Global' | 'Project'): void {
  scopeFilter.value = value
  crud.setFilters({ scope: value })
  crud.refresh().catch(() => undefined)
}

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
</script>

<style scoped>
.t-workspace-agent-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.t-workspace-agent-page__agent-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  transition: box-shadow 0.15s ease;
}
.t-workspace-agent-page__agent-card:hover {
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-workspace-agent-page__card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 2px;
}
.t-workspace-agent-page__card-name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-workspace-agent-page__card-id {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.t-workspace-agent-page__card-desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  margin-bottom: 6px;
}
.t-workspace-agent-page__card-provider {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.t-workspace-agent-page__card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-bottom: 8px;
}
.t-workspace-agent-page__card-actions {
  display: flex;
  gap: 6px;
  margin-top: 4px;
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
</style>
