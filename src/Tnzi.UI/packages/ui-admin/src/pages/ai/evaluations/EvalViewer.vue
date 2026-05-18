<template>
  <div class="t-eval-page t-page-scroll">
    <NCard :title="t('title')" :bordered="false">
      <template #header-extra>
        <NSpace>
          <NButton size="small" @click="refresh">{{ t('refresh') }}</NButton>
          <NButton size="small" type="primary" @click="newRunModal.show = true">
            {{ t('newRun') }}
          </NButton>
        </NSpace>
      </template>

      <p class="t-eval-page__note">{{ t('note') }}</p>

      <div class="t-eval-page__layout">
        <!-- Left: runs list (selectable for diff) -->
        <aside class="t-eval-page__list">
          <div class="t-eval-page__list-header">
            <span>{{ t('list.heading') }}</span>
            <span class="t-eval-page__hint">{{ t('list.diffHint') }}</span>
          </div>
          <NSpin :show="listLoading">
            <div v-if="!runs.length && !listLoading" class="t-eval-page__empty">
              {{ t('empty') }}
            </div>
            <ul v-else class="t-eval-page__run-list">
              <li
                v-for="run in runs"
                :key="run.id"
                class="t-eval-page__run-item"
                :class="{
                  'is-left': run.id === leftId,
                  'is-right': run.id === rightId,
                  'is-selected': run.id === leftId || run.id === rightId,
                }"
                @click="onPickRun(run.id)"
              >
                <div class="t-eval-page__run-row">
                  <NTag size="small" :type="statusTypeFor(run.status)" :bordered="false">
                    {{ run.status }}
                  </NTag>
                  <span class="t-eval-page__score" :data-pass="passRatio(run) >= 0.5">
                    {{ (passRatio(run) * 100).toFixed(0) }}%
                  </span>
                </div>
                <div class="t-eval-page__run-meta">
                  <code>{{ shortId(run.id) }}</code>
                  <span>{{ run.caseCount }} cases · avg {{ run.averageScore.toFixed(2) }}</span>
                </div>
                <div class="t-eval-page__run-time">{{ formatTime(run.creationTime) }}</div>
              </li>
            </ul>
          </NSpin>
        </aside>

        <!-- Right: detail / diff -->
        <section class="t-eval-page__detail">
          <div v-if="!leftId && !rightId" class="t-eval-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <div v-else class="t-eval-page__diff">
            <div class="t-eval-page__diff-col">
              <header class="t-eval-page__diff-head">
                <span class="t-eval-page__col-label is-left">A</span>
                <div v-if="leftDetail">
                  <strong>{{ shortId(leftDetail.id) }}</strong>
                  <span class="t-eval-page__col-score">
                    {{ (passRatio(leftDetail) * 100).toFixed(0) }}%
                    · avg {{ leftDetail.averageScore.toFixed(2) }}
                  </span>
                </div>
                <div v-else class="t-eval-page__placeholder">{{ t('pickLeft') }}</div>
              </header>
              <pre v-if="leftDetail?.resultsJson" class="t-eval-page__results">{{ formatJson(leftDetail.resultsJson) }}</pre>
            </div>
            <div class="t-eval-page__diff-col">
              <header class="t-eval-page__diff-head">
                <span class="t-eval-page__col-label is-right">B</span>
                <div v-if="rightDetail">
                  <strong>{{ shortId(rightDetail.id) }}</strong>
                  <span class="t-eval-page__col-score">
                    {{ (passRatio(rightDetail) * 100).toFixed(0) }}%
                    · avg {{ rightDetail.averageScore.toFixed(2) }}
                  </span>
                </div>
                <div v-else class="t-eval-page__placeholder">{{ t('pickRight') }}</div>
              </header>
              <pre v-if="rightDetail?.resultsJson" class="t-eval-page__results">{{ formatJson(rightDetail.resultsJson) }}</pre>
            </div>
          </div>
        </section>
      </div>
    </NCard>

    <!-- New run modal (kept simple — wraps the legacy create-and-run flow) -->
    <NModal v-model:show="newRunModal.show" :title="t('newRun')" preset="card" style="width: 560px">
      <NForm label-placement="left" label-width="120px">
        <NFormItem :label="t('form.agentId')" required>
          <NInput v-model:value="newRunModal.agentId" placeholder="GUID" />
        </NFormItem>
        <NFormItem :label="t('form.versionNumber')">
          <NInputNumber v-model:value="newRunModal.versionNumber" :min="1" clearable />
        </NFormItem>
        <NFormItem :label="t('form.caseInput')" required>
          <NInput v-model:value="newRunModal.caseInput" type="textarea" :rows="3" />
        </NFormItem>
        <NFormItem :label="t('form.caseExpected')">
          <NInput v-model:value="newRunModal.caseExpected" type="textarea" :rows="2" />
        </NFormItem>
      </NForm>
      <template #footer>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <NButton @click="newRunModal.show = false">{{ t('form.cancel') }}</NButton>
          <NButton
            type="primary"
            :loading="creating"
            :disabled="!newRunModal.agentId || !newRunModal.caseInput.trim()"
            @click="submitNewRun"
          >
            {{ t('form.runNow') }}
          </NButton>
        </div>
      </template>
    </NModal>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, watch, onMounted } from 'vue'
import {
  NCard, NSpace, NButton, NSpin, NTag, NModal, NForm, NFormItem, NInput, NInputNumber,
  useMessage,
} from 'naive-ui'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { translatePageKey } from '../../_shared/translate'
import type {
  CreateEvaluationRunDto,
  EvaluationRunDto,
  EvaluationRunDetailDto,
} from '@tnzi/core/services/ai'

const bridge = createAiBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('ai.evaluations', key)

let message: { success(s: string): void; error(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {} }
}

const runs = ref<EvaluationRunDto[]>([])
const listLoading = ref(false)
const leftId = ref<string | null>(null)
const rightId = ref<string | null>(null)
const leftDetail = ref<EvaluationRunDetailDto | null>(null)
const rightDetail = ref<EvaluationRunDetailDto | null>(null)
const creating = ref(false)

const newRunModal = reactive({
  show: false,
  agentId: '',
  versionNumber: null as number | null,
  caseInput: '',
  caseExpected: '',
})

function passRatio(r: EvaluationRunDto): number {
  if (!r.caseCount) return 0
  return r.passedCount / r.caseCount
}

function statusTypeFor(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Pending': return 'info'
    default: return 'default'
  }
}

function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id
}

function formatTime(v?: string | Date | null): string {
  if (!v) return ''
  try {
    return new Date(v).toLocaleString()
  } catch {
    return ''
  }
}

function formatJson(s: string | null | undefined): string {
  if (!s) return ''
  try {
    return JSON.stringify(JSON.parse(s), null, 2)
  } catch {
    return s
  }
}

async function loadList(): Promise<void> {
  listLoading.value = true
  try {
    const result = await bridge.evaluations.fetch({
      pageIndex: 1,
      pageSize: 50,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: {},
    })
    runs.value = result.items
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    runs.value = []
  } finally {
    listLoading.value = false
  }
}

// Click-to-pick: first click sets A, second sets B, third resets B then sets new B,
// clicking the same row again clears it.
function onPickRun(id: string): void {
  if (id === leftId.value) {
    leftId.value = null
    leftDetail.value = null
    return
  }
  if (id === rightId.value) {
    rightId.value = null
    rightDetail.value = null
    return
  }
  if (!leftId.value) {
    leftId.value = id
  } else {
    rightId.value = id
  }
}

async function loadDetail(id: string): Promise<EvaluationRunDetailDto | null> {
  try {
    return await bridge.evaluations.getDetail(id)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return null
  }
}

watch(leftId, async (id) => {
  leftDetail.value = id ? await loadDetail(id) : null
})
watch(rightId, async (id) => {
  rightDetail.value = id ? await loadDetail(id) : null
})

async function submitNewRun(): Promise<void> {
  creating.value = true
  try {
    const dto: CreateEvaluationRunDto = {
      agentId: newRunModal.agentId,
      versionNumber: newRunModal.versionNumber,
      cases: [
        {
          input: newRunModal.caseInput.trim(),
          expectedOutput: newRunModal.caseExpected.trim() || null,
        },
      ],
    }
    const created = await bridge.evaluations.create(dto)
    message.success(t('createSuccess'))
    newRunModal.show = false
    newRunModal.caseInput = ''
    newRunModal.caseExpected = ''
    await loadList()
    // Auto-pick the freshly created run on the left.
    leftId.value = (created as EvaluationRunDto).id
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    creating.value = false
  }
}

async function refresh(): Promise<void> {
  await loadList()
}

onMounted(() => {
  void loadList()
})
</script>

<style scoped>
.t-eval-page {
  padding: 16px;
}
.t-eval-page__note {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-eval-page__layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  min-height: 520px;
}
.t-eval-page__list {
  border-right: 1px solid var(--tnzi-base-border, #efeff5);
  padding-right: 16px;
}
.t-eval-page__list-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 8px;
}
.t-eval-page__hint {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-eval-page__run-list {
  list-style: none;
  padding: 0;
  margin: 0;
  max-height: 64vh;
  overflow: auto;
}
.t-eval-page__run-item {
  padding: 10px 12px;
  border: 2px solid transparent;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  margin-bottom: 6px;
  transition: background-color 0.15s, border-color 0.15s;
}
.t-eval-page__run-item:hover {
  background: var(--tnzi-base-fill, #f5f5f7);
}
.t-eval-page__run-item.is-selected {
  background: var(--tnzi-primary-color-suppl, rgba(6, 182, 212, 0.08));
}
.t-eval-page__run-item.is-left {
  border-color: var(--tnzi-primary-color, #06B6D4);
}
.t-eval-page__run-item.is-right {
  border-color: var(--tnzi-warning-color, #F0A020);
}
.t-eval-page__run-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.t-eval-page__score {
  font-weight: 600;
  font-size: 14px;
}
.t-eval-page__score[data-pass="true"] {
  color: var(--tnzi-success-color, #18A058);
}
.t-eval-page__score[data-pass="false"] {
  color: var(--tnzi-error-color, #D03050);
}
.t-eval-page__run-meta {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  display: flex;
  justify-content: space-between;
}
.t-eval-page__run-meta code {
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
}
.t-eval-page__run-time {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-top: 2px;
}
.t-eval-page__detail {
  padding: 0 4px;
}
.t-eval-page__placeholder,
.t-eval-page__empty {
  color: var(--tnzi-base-text-muted, #888);
  text-align: center;
  padding: 40px 16px;
  font-size: 13px;
}
.t-eval-page__diff {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.t-eval-page__diff-col {
  border: 1px solid var(--tnzi-base-border, #efeff5);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  padding: 12px;
  min-height: 480px;
  display: flex;
  flex-direction: column;
}
.t-eval-page__diff-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--tnzi-base-border, #efeff5);
  margin-bottom: 8px;
}
.t-eval-page__col-label {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  font-weight: 600;
  font-size: 12px;
  color: white;
}
.t-eval-page__col-label.is-left {
  background: var(--tnzi-primary-color, #06B6D4);
}
.t-eval-page__col-label.is-right {
  background: var(--tnzi-warning-color, #F0A020);
}
.t-eval-page__col-score {
  margin-left: 8px;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
}
.t-eval-page__results {
  flex: 1;
  margin: 0;
  font-size: 12px;
  font-family: var(--tnzi-font-family-mono, ui-monospace, monospace);
  background: var(--tnzi-base-fill, #f5f5f7);
  padding: 8px;
  border-radius: 4px;
  overflow: auto;
  max-height: 540px;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
