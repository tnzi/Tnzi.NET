<template>
  <div class="ai-thread-page">
    <TItemPage
      :state="crud"
      :search-fields="threadSearchFields"
      :title="title"
      :translate="t"
      :row-actions="rowActions"
      :detail-width="720"
      :detail-title="(d: AgentThreadDto) => d.title || t('detail.untitled')"
      :show-create="false"
      show-batch
    >
      <!-- A thread is a conversation, so the row leads with its title and shows
           which agent held it, how long it ran and when it was last touched.
           Opening the row shows the transcript. -->
      <template #item="{ item, selected, selectable, toggleSelect }">
        <TItemCard
          :title="item.title || t('detail.untitled')"
          icon="mdi:forum-outline"
          icon-tone="info"
          :meta="threadMeta(item)"
          :selectable="selectable"
          :checked="selected"
          :selected="selected"
          clickable
          @update:checked="toggleSelect"
          @click="crud.openView(item)"
        >
          <template #actions>
            <TRowActions :row="item" :actions="rowActions" :translate="t" />
          </template>
        </TItemCard>
      </template>

      <!-- Edit form - title only; threads aren't created here, they come from chat. -->
      <template #form="{ formData, mode }">
        <NForm :disabled="mode === 'view'">
          <NFormItem :label="t('form.title')" path="title">
            <NInput
              :value="titleValue(formData)"
              :placeholder="t('form.titleHint')"
              @update:value="(v: string) => setTitle(formData, v)"
            />
          </NFormItem>
        </NForm>
      </template>

      <!-- Read-only detail - thread header + full transcript (loaded by `onView`
           via getDetail). The row's `view` open-state drives the drawer; the
           heavier detail payload is page-local. -->
      <template #detail>
        <NSpin :show="detailLoading">
          <template v-if="detail">
            <!-- Header meta strip -->
            <div class="ai-thread-detail__meta">
              <div v-if="detail.agentName || detail.agentId">
                <strong>{{ t('detail.agent') }}:</strong>
                <span>{{ detail.agentName || detail.agentId }}</span>
              </div>
              <div>
                <strong>{{ t('detail.messageCount') }}:</strong>
                <span>{{ detail.messageCount }}</span>
              </div>
              <div>
                <strong>{{ t('detail.creationTime') }}:</strong>
                <span>{{ formatDateTime(detail.creationTime, { fallback: EMPTY_DASH }) }}</span>
              </div>
              <div v-if="detail.lastActivityTime">
                <strong>{{ t('detail.lastActivityTime') }}:</strong>
                <span>{{ formatDateTime(detail.lastActivityTime, { fallback: EMPTY_DASH }) }}</span>
              </div>
            </div>

            <!-- Message transcript -->
            <div class="ai-thread-detail__section">
              <h3>{{ t('detail.messages') }}</h3>
              <div v-if="detail.messages.length" class="ai-thread-detail__messages">
                <div
                  v-for="msg in detail.messages"
                  :key="msg.id"
                  class="ai-thread-msg"
                  :class="`ai-thread-msg--${roleVariant(msg.role)}`"
                >
                  <div class="ai-thread-msg__head flex items-center gap-8px mb-4px">
                    <NTag size="small" :type="roleTagType(msg.role)" :bordered="false">
                      {{ roleLabel(msg.role) }}
                    </NTag>
                    <span class="ai-thread-msg__time">
                      {{ formatDateTime(msg.creationTime, { fallback: EMPTY_DASH }) }}
                    </span>
                  </div>
                  <pre class="ai-thread-msg__body">{{ msg.content }}</pre>
                </div>
              </div>
              <div v-else class="ai-thread-detail__empty">{{ t('detail.empty') }}</div>
            </div>
          </template>

          <div v-else-if="detailError" class="ai-thread-detail__empty">
            {{ t('detail.loadError') }}
          </div>
        </NSpin>
      </template>

      <template #detailFooter>
        <div class="flex justify-end gap-8px">
          <NButton size="small" @click="exportJson(detailId)">{{ t('actions.exportJson') }}</NButton>
          <NButton size="small" @click="exportMd(detailId)">{{ t('actions.exportMd') }}</NButton>
        </div>
      </template>
    </TItemPage>
  </div>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { ref } from 'vue'
import { NButton, NForm, NFormItem, NInput, NSpin, NTag } from 'naive-ui'
import { downloadBlob, formatDateTime } from '@tnzi/core'
import TItemPage from '../../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardMeta } from '../../../components/data/TItemCard.vue'
import TRowActions from '../../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../../headless/rowActions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safeMessage'
import { makePageTranslator } from '../../_shared/translate'
import { threadColumns, threadSearchFields } from './thread-config'
import type { AgentThreadDto, AgentThreadDetailDto } from '@tnzi/core/services/ai'

const t = makePageTranslator('ai.threads')
const title = 'title'

const bridge = createAiBridge({ client: useAdminClient() })
const message = useSafeMessage()

// Threads are produced by conversations - no create. Edit updates the title;
// delete removes the thread (bridge.threads.update only sends `{ title }`).
const crud = useCrudPage<AgentThreadDto>({
  pageId: 'ai.threads',
  permission: 'ai.thread',
  columns: threadColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.threads.fetch(q),
  updateData: (id, data) =>
    bridge.threads.update(String(id), { title: (data as { title?: string }).title ?? '' }),
  deleteData: (ids) => bridge.threads.delete(ids.map(String)),
  onView: (row) => void loadThreadDetail(row),
})

// --- edit form (title only) -------------------------------------------------
function titleValue(formData: unknown): string {
  return ((formData as { title?: string } | null)?.title) ?? ''
}
function setTitle(formData: unknown, value: string): void {
  if (formData && typeof formData === 'object') {
    ;(formData as Record<string, unknown>).title = value
  }
}

// --- detail drawer ----------------------------------------------------------
// `detailVisible` is gone - the drawer rides the CRUD `view` open-state (row
// click → `crud.openView`, deep-linkable for free). Only the heavier
// transcript payload (a different DTO loaded via getDetail) stays page-local;
// `onView` loads it on open AND on a deep-link cold reload.
const detail = ref<AgentThreadDetailDto | null>(null)
const detailLoading = ref(false)
const detailError = ref(false)
const detailId = ref('')

async function loadThreadDetail(row: AgentThreadDto): Promise<void> {
  detailId.value = String(row.id)
  detail.value = null
  detailError.value = false
  detailLoading.value = true
  try {
    detail.value = await bridge.threads.getDetail(String(row.id))
  } catch {
    detail.value = null
    detailError.value = true
  } finally {
    detailLoading.value = false
  }
}

/** Row facts: which agent, how many turns, when it last moved. */
function threadMeta(row: AgentThreadDto): ItemCardMeta[] {
  const out: ItemCardMeta[] = []
  const agent = row.agentName || row.agentId
  if (agent) out.push({ icon: 'mdi:robot-outline', text: String(agent) })
  out.push({ icon: 'mdi:message-text-outline', text: String(row.messageCount ?? 0) })
  out.push({
    icon: 'mdi:clock-outline',
    text: formatDateTime(row.lastActivityTime ?? row.creationTime, { fallback: EMPTY_DASH }),
  })
  return out
}

// --- message role presentation ----------------------------------------------
function roleVariant(role: string): 'user' | 'assistant' | 'other' {
  const r = (role ?? '').toLowerCase()
  if (r === 'user') return 'user'
  if (r === 'assistant') return 'assistant'
  return 'other'
}
function roleTagType(role: string): 'info' | 'success' | 'default' {
  const v = roleVariant(role)
  return v === 'user' ? 'info' : v === 'assistant' ? 'success' : 'default'
}
function roleLabel(role: string): string {
  const v = roleVariant(role)
  if (v === 'user') return t('detail.role.user')
  if (v === 'assistant') return t('detail.role.assistant')
  return role || t('detail.role.other')
}

// --- exports ----------------------------------------------------------------
async function exportJson(id: string): Promise<void> {
  if (!id) return
  try {
    const data = await bridge.threads.exportJson(id)
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    downloadBlob(blob, `thread-${id}.json`)
    message.success(t('actions.exportJsonSuccess'))
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

function exportMd(id: string): void {
  if (!id) return
  // The MD export is a server file download - hand the browser the URL.
  const a = document.createElement('a')
  a.href = bridge.threads.getExportMarkdownUrl(id)
  a.download = `thread-${id}.md`
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
}

// --- declarative row actions (C5) -------------------------------------------
// The row click opens the transcript; these are the operations that a click
// cannot express. Export JSON/MD download; Edit renames; Delete removes.
const rowActions: RowAction<AgentThreadDto>[] = [
  // No View action: the row itself opens the transcript drawer.
  { key: 'exportJson', label: 'actions.exportJson', onClick: (row) => void exportJson(String(row.id)) },
  { key: 'exportMd', label: 'actions.exportMd', onClick: (row) => exportMd(String(row.id)) },
  editAction(crud),
  deleteAction(crud),
]

// Expose the bridge-driven handlers + drawer state to the integration test -
// they're only referenced inside `rowActions`, so they wouldn't be auto-exposed
// on `wrapper.vm` otherwise (the template-referenced `crud` already is).
defineExpose({ crud, loadThreadDetail, exportJson, exportMd, detail })
</script>

<style scoped>
.ai-thread-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.ai-thread-detail__meta {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--tnzi-border, #e5e7eb);
}
.ai-thread-detail__meta strong {
  margin-right: 6px;
}
.ai-thread-detail__section {
  margin-top: 16px;
}
.ai-thread-detail__section h3 {
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ai-thread-detail__empty {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  padding: 16px 8px;
  text-align: center;
}
.ai-thread-detail__messages {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.ai-thread-msg {
  border-radius: 8px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-border, #e5e7eb);
  background: var(--tnzi-bg-deep, #f6f8fa);
}
.ai-thread-msg--user {
  border-left: 3px solid var(--tnzi-info, #2080f0);
}
.ai-thread-msg--assistant {
  border-left: 3px solid var(--tnzi-success, #18a058);
}
.ai-thread-msg__time {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #999);
}
.ai-thread-msg__body {
  margin: 0;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 40vh;
  overflow: auto;
}
</style>
