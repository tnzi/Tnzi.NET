<template>
  <!--
    WorkspaceAgents - read-only view of Agent definitions discovered from
    AGENT.md / PERSONA.md files on disk. A standard TCardPage (mode="page"):
    the shell owns the white header (title + help + keyword search), the scope
    filter (All / Global / Project) is a segmented control in the toolbar, and
    each card opens a read-only detail drawer (deep-linkable via the CRUD `view`
    open-state). No create/edit/delete - the page is read-only, so those
    affordances are hidden automatically (no create/update/delete callbacks).
  -->
  <TCardPage
    :state="crud"
    mode="page"
    :title="t('title')"
    :title-help="t('banner')"
    :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
    :search-placeholder="t('search.placeholder')"
    :show-pagination="false"
    :detail-width="640"
    :detail-title="(d: WorkspaceAgentDto) => d.name"
    :translate="t"
  >
    <!-- Scope filter - a segmented control (All / Global / Project). It drives
         the fetch query's `scope` filter; it is NOT navigation, so it stays a
         toolbar control rather than a deep-linked tab. -->
    <template #toolbarLeft>
      <NRadioGroup :value="scopeFilter" size="small" @update:value="onScopeChange">
        <NRadioButton value="all">{{ t('scope.all') }}</NRadioButton>
        <NRadioButton value="Global">{{ t('scope.global') }}</NRadioButton>
        <NRadioButton value="Project">{{ t('scope.project') }}</NRadioButton>
      </NRadioGroup>
    </template>

    <template #card="{ item }">
      <TEntityCard clickable @click="crud.openView(item)">
        <div class="flex items-center justify-between gap-8px mb-2px">
          <span class="t-wsa__name flex-1 min-w-0">{{ item.name }}</span>
          <NTag
            size="small"
            :type="item.workspaceScope === 'Project' ? 'warning' : 'info'"
            :bordered="false"
            class="flex-shrink-0"
          >
            {{ item.workspaceScope === 'Project' ? t('scope.project') : t('scope.global') }}
          </NTag>
        </div>
        <div class="t-wsa__id font-mono">{{ item.agentId }}</div>
        <div class="t-wsa__desc">{{ item.description || EMPTY_DASH }}</div>
        <div v-if="item.provider" class="t-wsa__provider">
          {{ item.provider }}{{ item.model ? ` · ${item.model}` : '' }}
        </div>
        <div v-if="hasTags(item)" class="flex flex-wrap gap-4px mt-4px">
          <NTag v-for="d in (item.domains ?? [])" :key="`d-${d}`" size="small" type="info" :bordered="false">{{ d }}</NTag>
          <NTag v-for="r in (item.roles ?? [])" :key="`r-${r}`" size="small" type="success" :bordered="false">{{ r }}</NTag>
          <NTag v-if="item.hasPersona" size="small" type="success" :bordered="false">{{ t('detail.persona') }}</NTag>
        </div>
        <template #actions>
          <NButton size="small" ghost @click="copyPath(item.filePath)">
            <template #icon><TSvgIcon icon="mdi:content-copy" :size="14" /></template>
            {{ t('actions.copyPath') }}
          </NButton>
        </template>
      </TEntityCard>
    </template>

    <!-- Read-only view of AGENT.md + PERSONA.md content. The card's `view`
         open-state drives this drawer (deep-linkable for free). -->
    <template #detail>
      <template v-if="viewed">
        <div class="t-wsa-detail__meta">
          <div class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.agentId') }}</span>
            <code>{{ viewed.agentId }}</code>
          </div>
          <div class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.scope') }}</span>
            <span>{{ viewed.workspaceScope }}</span>
          </div>
          <div class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.filePath') }}</span>
            <code>{{ viewed.filePath }}</code>
          </div>
          <div v-if="viewed.provider" class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.provider') }}</span>
            <span>{{ viewed.provider }}{{ viewed.model ? ` · ${viewed.model}` : '' }}</span>
          </div>
          <div v-if="viewed.executionMode" class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.executionMode') }}</span>
            <span>{{ viewed.executionMode }}</span>
          </div>
          <div v-if="(viewed.toolGroups?.length ?? 0) > 0" class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.toolGroups') }}</span>
            <span class="flex flex-wrap gap-4px">
              <NTag v-for="g in (viewed.toolGroups ?? [])" :key="g" size="small" :bordered="false">{{ g }}</NTag>
            </span>
          </div>
          <div v-if="viewed.description" class="t-wsa-detail__row">
            <span class="t-wsa-detail__k">{{ t('detail.description') }}</span>
            <span>{{ viewed.description }}</span>
          </div>
        </div>

        <div class="t-wsa-detail__section">
          <h3>{{ t('detail.instructions') }}</h3>
          <pre class="t-wsa-detail__body">{{ viewed.instructions || t('detail.noInstructions') }}</pre>
        </div>

        <div v-if="viewed.personaContent" class="t-wsa-detail__section">
          <h3>{{ t('detail.persona') }}</h3>
          <pre class="t-wsa-detail__body">{{ viewed.personaContent }}</pre>
        </div>
      </template>
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, ref } from 'vue'
import { NButton, NRadioButton, NRadioGroup, NTag, useMessage } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../../plugin/client'
import { createWorkspaceAgentsBridge } from '../../../services/bridges/ai-bridge'
import type { WorkspaceAgentDto } from '@tnzi/core/services/ai'
import { makePageTranslator } from '../../_shared/translate'
import { useCrudPage } from '../../../headless/useCrudPage'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'

const t = makePageTranslator('ai.workspaceAgents')
const message = (() => {
  try { return useMessage() } catch { return null }
})()

const bridge = createWorkspaceAgentsBridge({ client: useAdminClient() })

const scopeFilter = ref<'all' | 'Global' | 'Project'>('all')

const crud = useCrudPage<WorkspaceAgentDto>({
  pageId: 'ai.workspace-agents',
  columns: [], // card page renders via #card slot; column defs unused
  rowKey: (r) => r.agentId,
  fetchData: async (q) => {
    const all = await bridge.list()
    const scope = q.filters.scope as string | undefined
    const kw = (q.searchText ?? '').trim().toLowerCase()
    let items = scope && scope !== 'all'
      ? all.filter((a) => a.workspaceScope === scope)
      : all
    if (kw) {
      items = items.filter(
        (a) =>
          a.name.toLowerCase().includes(kw) ||
          a.agentId.toLowerCase().includes(kw) ||
          (a.description ?? '').toLowerCase().includes(kw),
      )
    }
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

function onScopeChange(value: string | number): void {
  const scope = String(value) as 'all' | 'Global' | 'Project'
  scopeFilter.value = scope
  crud.setFilters({ scope })
  crud.refresh().catch(() => undefined)
}

function hasTags(item: WorkspaceAgentDto): boolean {
  return (item.domains?.length ?? 0) > 0 || (item.roles?.length ?? 0) > 0 || Boolean(item.hasPersona)
}

async function copyPath(path: string): Promise<void> {
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      await navigator.clipboard.writeText(path)
      message?.success(t('actions.copied'))
    }
  } catch {
    // ignore - clipboard access may be denied in some contexts
  }
}

// The viewed agent IS the CRUD `view` open-state (card click → `crud.openView`),
// so it deep-links to `?detail=view:<id>` for free. Every field is already on
// the row - no lazy load, hence no `onView`.
const viewed = computed(() => crud.formModal.formData.value as WorkspaceAgentDto | null)
</script>

<style scoped>
/* Card body - only the ellipsis / line-clamp / mono treatments that unocss
   atomic classes can't express live here (per C7). */
.t-wsa__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-wsa__id {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-wsa__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  margin-bottom: 6px;
}
.t-wsa__provider {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}

/* Detail drawer */
.t-wsa-detail__meta {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.t-wsa-detail__row {
  display: flex;
  gap: 10px;
  font-size: 13px;
  line-height: 1.5;
}
.t-wsa-detail__k {
  flex-shrink: 0;
  width: 108px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-wsa-detail__section {
  margin-top: 16px;
}
.t-wsa-detail__section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-wsa-detail__body {
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
