<template>
  <div class="ai-knowledge-page">
    <TCardPage
      :state="crud"
      mode="page"
      :title="t('title')"
      :title-help="t('banner')"
      :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
      :search-placeholder="t('search.placeholder')"
      :form-modal-width="640"
      :translate="t"
    >
      <template #card="{ item }">
        <TEntityCard clickable @click="openManage(item)">
          <div class="flex items-center gap-8px mb-6px">
            <span class="ai-knowledge-card__icon">
              <TSvgIcon icon="mdi:book-open-variant" :size="18" />
            </span>
            <span class="ai-knowledge-card__name flex-1">{{ item.name }}</span>
            <NTag size="small" :type="item.isEnabled ? 'success' : 'default'" :bordered="false">
              {{ item.isEnabled ? t('badge.enabled') : t('badge.disabled') }}
            </NTag>
          </div>

          <div class="ai-knowledge-card__stats">
            <span>{{ t('badge.documents') }}: <strong>{{ item.documentCount ?? 0 }}</strong></span>
            <span>{{ t('badge.chunks') }}: <strong>{{ item.chunkCount ?? 0 }}</strong></span>
          </div>

          <div v-if="item.embeddingProvider || item.embeddingModel" class="ai-knowledge-card__embed font-mono">
            {{ item.embeddingProvider }}{{ item.embeddingModel ? ` · ${item.embeddingModel}` : '' }}
          </div>

          <div class="ai-knowledge-card__desc">{{ item.description || t('noDescription') }}</div>

          <template #actions>
            <NPopconfirm @positive-click="onReindex(item)">
              <template #trigger>
                <NButton size="small" ghost :loading="reindexBusyId === item.id">
                  {{ t('actions.reindex') }}
                </NButton>
              </template>
              {{ t('reindex.confirm') }}
            </NPopconfirm>
            <NButton size="small" ghost @click="crud.openEdit(item)">
              {{ t('actions.edit') }}
            </NButton>
            <NPopconfirm @positive-click="removeOne(item)">
              <template #trigger>
                <NButton size="small" type="error" ghost>{{ t('actions.delete') }}</NButton>
              </template>
              {{ t('deleteConfirm') }}
            </NPopconfirm>
          </template>
        </TEntityCard>
      </template>

      <template #form="{ formData, mode }">
        <TFormSchemaRenderer
          :schema="mode === 'edit' ? knowledgeEditFormSchema : knowledgeCreateFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :readonly="mode === 'view'"
          :translate="t"
        />
      </template>
    </TCardPage>

    <!--
      Document management drawer — per knowledge base: documents + search test.
      A custom secondary surface driven by `useDetail` (mode 'drawer') + rendered
      by the single `TDetailHost` renderer, so it is deep-linkable
      (`?manage=view:<kbId>`), refresh-survivable and Back-closeable for free.
      `:footer="false"` — the panel's controls live in the body; the X closes it.
    -->
    <TDetailHost :state="manageDetail" :title="managed?.name ?? ''" :width="720" :footer="false" :translate="t">
      <template #default>
        <NTabs v-if="managed" v-model:value="manageTab" type="line" animated>
          <!-- (A) Documents ------------------------------------------------ -->
          <NTabPane name="documents" :tab="t('manage.documentsTab')">
            <div class="ai-knowledge-upload">
              <input
                ref="fileInputRef"
                type="file"
                class="ai-knowledge-upload__input"
                @change="onFilePicked"
              />
              <NButton
                size="small"
                type="primary"
                :loading="uploadBusy"
                @click="triggerFilePick"
              >
                {{ t('upload.button') }}
              </NButton>
              <span v-if="uploadStatus" class="ai-knowledge-upload__status" :data-kind="uploadStatus.kind">
                {{ uploadStatus.message }}
              </span>
            </div>

            <TResponsiveTable
              :columns="documentColumns"
              :data="documents"
              :loading="documentsLoading"
              :row-key="(row: KnowledgeDocumentDto) => row.id"
              :pagination="false"
              :empty-text="t('manage.noDocuments')"
              :row-actions="documentRowActions"
              :row-actions-title="t('columns.actions')"
              :translate="t"
              size="small"
            />
          </NTabPane>

          <!-- (B) Search test ---------------------------------------------- -->
          <NTabPane name="search" :tab="t('manage.searchTab')">
            <div class="ai-knowledge-search">
              <NInput
                v-model:value="searchQuery"
                size="small"
                class="flex-1"
                :placeholder="t('search.queryPlaceholder')"
                @keyup.enter="onSearchTest"
              />
              <NInputNumber
                v-model:value="searchTopK"
                size="small"
                :min="1"
                :max="50"
                class="w-110px"
                :placeholder="t('search.topK')"
              />
              <NButton size="small" type="primary" :loading="searchBusy" @click="onSearchTest">
                {{ t('search.button') }}
              </NButton>
            </div>

            <div v-if="searchResults.length" class="ai-knowledge-search__results">
              <div
                v-for="(hit, idx) in searchResults"
                :key="idx"
                class="ai-knowledge-search__hit"
              >
                <div class="ai-knowledge-search__hit-head">
                  <span class="ai-knowledge-search__hit-source">{{ hit.sourceName || t('search.unknownSource') }}</span>
                  <span class="ai-knowledge-search__hit-score">{{ formatScore(hit.score) }}</span>
                </div>
                <NProgress
                  type="line"
                  :percentage="Math.round((hit.score ?? 0) * 100)"
                  :show-indicator="false"
                  :height="4"
                />
                <div class="ai-knowledge-search__hit-content">{{ truncate(hit.content) }}</div>
              </div>
            </div>
            <div v-else-if="searchedOnce && !searchBusy" class="ai-knowledge-search__empty">
              {{ t('search.noResults') }}
            </div>
          </NTabPane>
        </NTabs>
      </template>
    </TDetailHost>
  </div>
</template>

<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import {
  NButton,
  NInput,
  NInputNumber,
  NPopconfirm,
  NProgress,
  NTabPane,
  NTabs,
  NTag,
  type DataTableColumns,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime, formatFileSize } from '@tnzi/core'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { useDetail } from '../../../headless/useDetail'
import type { RowAction } from '../../../headless/rowActions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { makePageTranslator } from '../../_shared/translate'
import {
  knowledgeCreateFormSchema,
  knowledgeEditFormSchema,
} from './knowledge-config'
import type {
  KnowledgeBaseDto,
  KnowledgeBaseCreateParams,
  KnowledgeBaseUpdateParams,
  KnowledgeDocumentDto,
  SearchResultDto,
} from '@tnzi/core/services/ai'

const t = makePageTranslator('ai.knowledge')

// ui-admin does not wrap the shell with NMessageProvider in every host, so
// `useMessage()` may throw — guard and fall back to inline status panels.
const message = (() => {
  try {
    return useMessage()
  } catch {
    return null
  }
})()

const bridge = createAiBridge({ client: useAdminClient() })

const crud = useCrudPage<KnowledgeBaseDto>({
  pageId: 'ai.knowledge',
  columns: [],
  rowKey: (row) => row.id,
  fetchData: (query) => bridge.knowledge.fetch(query),
  createData: (data) => bridge.knowledge.create(data as KnowledgeBaseCreateParams),
  updateData: (id, data) => bridge.knowledge.update(String(id), data as KnowledgeBaseUpdateParams),
  deleteData: (ids) => bridge.knowledge.delete(ids.map(String)),
})

async function removeOne(item: KnowledgeBaseDto): Promise<void> {
  await crud.handleDelete([item.id])
}

// --- reindex ---------------------------------------------------------------
const reindexBusyId = ref<string | null>(null)

async function onReindex(item: KnowledgeBaseDto): Promise<void> {
  if (reindexBusyId.value) return
  reindexBusyId.value = item.id
  try {
    const result = await bridge.knowledge.reindex(item.id)
    message?.success(
      `${result.chunkCount} ${t('reindex.chunks')} / ${result.documentCount} ${t('reindex.docs')} / ${result.durationMs}ms`,
    )
    await crud.refresh()
  } catch (err) {
    message?.error(err instanceof Error ? err.message : t('reindex.error'))
  } finally {
    reindexBusyId.value = null
  }
}

// --- document management drawer --------------------------------------------
// A custom secondary overlay driven by the single detail engine (`useDetail`,
// mode 'drawer') — deep-linked as `?manage=view:<kbId>`, refresh-survivable and
// Back-closeable for free. `source: crud` hydrates a cold-load deep link from
// the loaded list, waiting for the first fetch automatically.
const manageDetail = useDetail<KnowledgeBaseDto>({
  mode: 'drawer',
  url: 'manage',
  source: crud,
})
const managed = computed(() => manageDetail.data.value)
const manageVisible = manageDetail.visible
const manageTab = ref<'documents' | 'search'>('documents')

const documents = ref<KnowledgeDocumentDto[]>([])
const documentsLoading = ref(false)

async function openManage(row: KnowledgeBaseDto): Promise<void> {
  await manageDetail.open('view', row)
}

// Reset the panel + load documents whenever the drawer (re)binds to a KB —
// covers in-session open AND a `#manage:view:<id>` deep link / refresh.
watch(() => manageDetail.data.value, async (kb) => {
  if (!kb) return
  manageTab.value = 'documents'
  uploadStatus.value = null
  searchResults.value = []
  searchedOnce.value = false
  await loadDocuments()
})

async function loadDocuments(): Promise<void> {
  const kb = manageDetail.data.value
  if (!kb) return
  documentsLoading.value = true
  try {
    const page = await bridge.knowledge.getDocuments(kb.id)
    documents.value = page.items
  } catch {
    documents.value = []
  } finally {
    documentsLoading.value = false
  }
}

// DocumentStatus arrives as the PascalCase member name (JsonStringEnumConverter)
// or, for legacy payloads, the numeric ordinal — normalise both to a stable
// member-name key so the status badge + processing-row detection stay correct.
type DocStatusName = 'Processing' | 'Completed' | 'Failed'
function documentStatusName(status: unknown): DocStatusName {
  const map: Record<string, DocStatusName> = {
    '0': 'Processing',
    '1': 'Completed',
    '2': 'Failed',
    Processing: 'Processing',
    Completed: 'Completed',
    Failed: 'Failed',
  }
  return map[String(status)] ?? 'Processing'
}

const STATUS_META: Record<DocStatusName, { type: 'default' | 'success' | 'error'; key: string }> = {
  Processing: { type: 'default', key: 'status.processing' },
  Completed: { type: 'success', key: 'status.completed' },
  Failed: { type: 'error', key: 'status.failed' },
}

const documentColumns: DataTableColumns<KnowledgeDocumentDto> = [
  { key: 'fileName', title: () => t('columns.fileName'), minWidth: 160 },
  {
    key: 'status',
    title: () => t('columns.status'),
    width: 150,
    render: (row) => {
      const name = documentStatusName(row.status)
      const meta = STATUS_META[name]
      const tag = h(
        NTag,
        { size: 'small', type: meta.type, bordered: false },
        { default: () => t(meta.key) },
      )
      if (name !== 'Processing') return tag
      // Processing rows expose a "Refresh status" action to re-poll.
      return h('div', { class: 'flex items-center gap-6px' }, [
        tag,
        h(
          NButton,
          {
            size: 'tiny',
            text: true,
            type: 'primary',
            onClick: () => refreshDocStatus(row),
          },
          { default: () => t('manage.refreshStatus') },
        ),
      ])
    },
  },
  { key: 'chunkCount', title: () => t('columns.chunkCount'), width: 90 },
  {
    key: 'fileSize',
    title: () => t('columns.fileSize'),
    width: 110,
    render: (row) => formatFileSize(row.fileSize ?? 0),
  },
  {
    key: 'creationTime',
    title: () => t('columns.creationTime'),
    width: 170,
    render: (row) => formatDateTime(row.creationTime),
  },
]

// Declarative row action for the document table — TResponsiveTable synthesises
// the trailing operation column (and folds it into the mobile card footer).
const documentRowActions: RowAction<KnowledgeDocumentDto>[] = [
  {
    key: 'delete',
    label: 'actions.delete',
    type: 'error',
    confirm: 'manage.deleteDocConfirm',
    onClick: (row) => deleteDoc(row),
  },
]

async function refreshDocStatus(row: KnowledgeDocumentDto): Promise<void> {
  if (!managed.value) return
  try {
    const updated = await bridge.knowledge.getDocumentStatus(managed.value.id, row.id)
    documents.value = documents.value.map((d) => (d.id === updated.id ? updated : d))
  } catch {
    // ignore — transient polling failure; the row keeps its prior status.
  }
}

async function deleteDoc(row: KnowledgeDocumentDto): Promise<void> {
  if (!managed.value) return
  try {
    await bridge.knowledge.deleteDocument(managed.value.id, row.id)
    await loadDocuments()
  } catch (err) {
    message?.error(err instanceof Error ? err.message : t('manage.deleteDocError'))
  }
}

// --- upload ----------------------------------------------------------------
const fileInputRef = ref<HTMLInputElement | null>(null)
const uploadBusy = ref(false)
const uploadStatus = ref<{ kind: 'success' | 'error'; message: string } | null>(null)

function triggerFilePick(): void {
  fileInputRef.value?.click()
}

async function onFilePicked(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file || !managed.value) return
  uploadBusy.value = true
  uploadStatus.value = null
  try {
    const result = await bridge.knowledge.uploadDocument(managed.value.id, file)
    const statusLabel = t(STATUS_META[documentStatusName(result.status)].key)
    uploadStatus.value = {
      kind: 'success',
      message: result.isDuplicate
        ? t('upload.duplicate')
        : `${t('upload.success')} (${statusLabel})`,
    }
    await loadDocuments()
  } catch (err) {
    uploadStatus.value = {
      kind: 'error',
      message: err instanceof Error ? err.message : t('upload.error'),
    }
  } finally {
    uploadBusy.value = false
    // Reset so the same file can be re-picked to trigger another upload.
    input.value = ''
  }
}

// --- search test -----------------------------------------------------------
const searchQuery = ref('')
const searchTopK = ref<number>(5)
const searchBusy = ref(false)
const searchedOnce = ref(false)
const searchResults = ref<SearchResultDto[]>([])

async function onSearchTest(): Promise<void> {
  if (!managed.value || !searchQuery.value.trim() || searchBusy.value) return
  searchBusy.value = true
  searchedOnce.value = true
  try {
    searchResults.value = await bridge.knowledge.searchTest(managed.value.id, {
      query: searchQuery.value.trim(),
      topK: searchTopK.value ?? 5,
    })
  } catch (err) {
    searchResults.value = []
    message?.error(err instanceof Error ? err.message : t('search.error'))
  } finally {
    searchBusy.value = false
  }
}

// --- formatting helpers ----------------------------------------------------
// formatFileSize is imported from @tnzi/core (single source of truth).
function formatScore(score: number | null | undefined): string {
  return `${Math.round((score ?? 0) * 100)}%`
}

function truncate(text: string | null | undefined, max = 240): string {
  const value = (text ?? '').trim()
  return value.length > max ? `${value.slice(0, max)}…` : value
}

defineExpose({ openManage, onReindex, reindexBusyId, manageVisible, managed })
</script>

<style scoped>
.ai-knowledge-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.ai-knowledge-card__icon {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.ai-knowledge-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-knowledge-card__stats {
  display: flex;
  gap: 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.ai-knowledge-card__embed {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-knowledge-card__desc {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 36px;
}
.ai-knowledge-upload {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}
.ai-knowledge-upload__input {
  display: none;
}
.ai-knowledge-upload__status[data-kind='error'] {
  color: var(--tnzi-error, #d03050);
  font-size: 12px;
}
.ai-knowledge-upload__status[data-kind='success'] {
  color: var(--tnzi-success, #18a058);
  font-size: 12px;
}
.ai-knowledge-search {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}
.ai-knowledge-search__results {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.ai-knowledge-search__hit {
  padding: 10px 12px;
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: 6px;
  background: var(--tnzi-bg-deep, #f6f8fa);
}
.ai-knowledge-search__hit-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
}
.ai-knowledge-search__hit-source {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-knowledge-search__hit-score {
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-primary, #6d5ce7);
  flex-shrink: 0;
}
.ai-knowledge-search__hit-content {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, #555);
  white-space: pre-wrap;
  word-break: break-word;
}
.ai-knowledge-search__empty {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  padding: 12px 0;
}
</style>
