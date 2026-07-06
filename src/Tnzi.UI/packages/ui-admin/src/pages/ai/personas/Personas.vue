<template>
  <div class="ai-persona-page">
    <TCardPage
      :state="crud"
      mode="page"
      :title="t('title')"
      :title-help="t('banner')"
      :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
      :search-placeholder="t('search.placeholder')"
      :form-modal-width="720"
      :detail-width="640"
      :detail-title="(d: AgentPersonaDto) => d.name"
      :translate="t"
    >
      <template #card="{ item }">
        <TEntityCard clickable @click="crud.openView(item)">
          <div class="flex items-center gap-8px mb-6px">
            <span class="ai-persona-card__icon">
              <TSvgIcon icon="mdi:drama-masks" :size="18" />
            </span>
            <span class="ai-persona-card__name flex-1">{{ item.name }}</span>
            <NTag size="small" :type="isSystemScope(item) ? 'warning' : 'info'" :bordered="false">
              {{ isSystemScope(item) ? t('badge.system') : t('badge.custom') }}
            </NTag>
          </div>
          <div class="ai-persona-card__slug font-mono">{{ item.slug }}</div>
          <div class="ai-persona-card__desc">{{ item.description || t('noDescription') }}</div>
          <div class="ai-persona-card__snippet font-mono">{{ contentPreview(item.content) }}</div>
          <template #actions>
            <NButton size="small" ghost :disabled="isLocked(item)" @click="crud.openEdit(item)">
              {{ t('actions.edit') }}
            </NButton>
            <NPopconfirm :disabled="isLocked(item)" @positive-click="removeOne(item)">
              <template #trigger>
                <NButton size="small" type="error" ghost :disabled="isLocked(item)">{{ t('actions.delete') }}</NButton>
              </template>
              {{ t('deleteConfirm') }}
            </NPopconfirm>
          </template>
        </TEntityCard>
      </template>

      <template #form="{ formData, mode }">
        <TFormSchemaRenderer
          :schema="personaFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :readonly="mode === 'view'"
          :translate="t"
        />
      </template>

      <!-- Read-only detail — full soul/system-prompt body + agents using this
           persona. Same `view` open-state as the form modal (so it is
           deep-linkable + Back-closeable for free); the engine renders it as a
           right drawer because this `#detail` slot exists. -->
      <template #detail>
        <template v-if="viewed">
          <div class="ai-persona-detail__meta">
            <div>
              <strong>{{ t('detail.slug') }}:</strong> <code>{{ viewed.slug }}</code>
            </div>
            <div>
              <strong>{{ t('detail.type') }}:</strong>
              <NTag size="small" :type="isSystemScope(viewed) ? 'warning' : 'info'" :bordered="false">
                {{ isSystemScope(viewed) ? t('badge.system') : t('badge.custom') }}
              </NTag>
            </div>
            <div v-if="viewed.description">
              <strong>{{ t('detail.description') }}:</strong> {{ viewed.description }}
            </div>
            <div v-if="viewed.lastModificationTime">
              <strong>{{ t('detail.lastModified') }}:</strong> {{ formatDateTime(viewed.lastModificationTime) }}
            </div>
          </div>

          <div class="ai-persona-detail__section">
            <h3>{{ t('detail.usedBy') }}</h3>
            <NSpin :show="relatedLoading" size="small">
              <div v-if="relatedAgents.length" class="flex flex-wrap gap-6px">
                <NTag v-for="a in relatedAgents" :key="a.id" size="small" :bordered="false">
                  {{ a.name }}
                </NTag>
              </div>
              <div v-else class="ai-persona-detail__empty">{{ t('detail.noAgents') }}</div>
            </NSpin>
          </div>

          <div class="ai-persona-detail__section">
            <h3>{{ t('detail.content') }}</h3>
            <pre class="ai-persona-detail__body">{{ viewed.content || t('detail.noContent') }}</pre>
          </div>
        </template>
      </template>

      <template #detailFooter>
        <NButton v-if="viewed" size="small" type="primary" :disabled="isLocked(viewed)" @click="crud.openEdit(viewed)">
          {{ t('actions.edit') }}
        </NButton>
      </template>
    </TCardPage>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton, NPopconfirm, NSpin, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { makePageTranslator } from '../../_shared/translate'
import { personaFormSchema, resourceScopeName } from './persona-config'
import type {
  AgentDto,
  AgentPersonaDto,
  CreateAgentPersonaDto,
  UpdateAgentPersonaDto,
} from '@tnzi/core/services/ai'
import { useAdminAuthStore } from '../../../stores/useAdminAuthStore'

const t = makePageTranslator('ai.personas')

const bridge = createAiBridge({ client: useAdminClient() })
const authStore = useAdminAuthStore()

function isSystemScope(p: AgentPersonaDto | null | undefined): boolean {
  // `scope` arrives as the PascalCase member name (JsonStringEnumConverter) or,
  // for legacy payloads, the numeric ordinal — normalise both.
  return !!p && resourceScopeName(p.scope) === 'System'
}

// Mirrors the backend write guard (AgentPersonaService Update/Delete): system personas are
// locked only for tenant-scoped sessions; without tenant context (host admin / MT off) they
// remain editable.
function isLocked(p: AgentPersonaDto | null | undefined): boolean {
  return isSystemScope(p) && !!authStore.currentTenantId
}

// --- detail drawer + related agents ----------------------------------------
// The viewed record IS the CRUD `view` open-state — no second ref. Opening it
// (card click → `crud.openView`) deep-links to `#detail:view:<id>` for free;
// `onView` lazy-loads the related agents (also on a deep-link cold reload).
const relatedAgents = ref<AgentDto[]>([])
const relatedLoading = ref(false)

const crud = useCrudPage<AgentPersonaDto>({
  pageId: 'ai.personas',
  columns: [],
  rowKey: (p) => p.id,
  fetchData: (query) => bridge.personas.fetch(query),
  createData: (data) => bridge.personas.create(data as CreateAgentPersonaDto),
  updateData: (id, data) => bridge.personas.update(String(id), data as UpdateAgentPersonaDto),
  deleteData: (ids) => bridge.personas.delete(ids.map(String)),
  onView: (row) => void loadRelatedAgents(row),
})

const viewed = computed(() => crud.formModal.formData.value as AgentPersonaDto | null)

function contentPreview(content: string | null | undefined): string {
  const text = (content ?? '').replace(/\s+/g, ' ').trim()
  return text.length > 120 ? `${text.slice(0, 120)}…` : text || '—'
}

async function removeOne(item: AgentPersonaDto): Promise<void> {
  await crud.handleDelete([item.id])
}

async function loadRelatedAgents(row: AgentPersonaDto): Promise<void> {
  relatedAgents.value = []
  relatedLoading.value = true
  try {
    // AgentListQueryDto has no personaId filter — pull a wide page and filter client-side.
    const page = await bridge.agents.fetch({ pageIndex: 1, pageSize: 200, searchText: '', filters: {} })
    relatedAgents.value = page.items.filter((a) => a.personaId === row.id)
  } catch {
    relatedAgents.value = []
  } finally {
    relatedLoading.value = false
  }
}
</script>

<style scoped>
.ai-persona-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.ai-persona-card__icon {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.ai-persona-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-persona-card__slug {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.ai-persona-card__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 36px;
}
.ai-persona-card__snippet {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #aaa);
  line-height: 1.4;
  margin-top: 6px;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.ai-persona-detail__meta {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.ai-persona-detail__section {
  margin-top: 16px;
}
.ai-persona-detail__section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ai-persona-detail__empty {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
}
.ai-persona-detail__body {
  margin: 0;
  padding: 12px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  border-radius: 6px;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 50vh;
  overflow: auto;
}
</style>
