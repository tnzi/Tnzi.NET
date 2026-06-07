<template>
  <TContentPage
    :title="t('title')"
    :translate="t"
    scroll="fill"
    card
  >
    <template #actions>
      <NButton size="small" @click="refresh">{{ t('refresh') }}</NButton>
      <NButton size="small" type="primary" @click="openNewRunModal">
        {{ t('newRun') }}
      </NButton>
    </template>

    <template #default>
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

      <!-- New run modal — uses TFormModal so width auto-adapts on
           narrow viewports (Phase 1 responsive). -->
      <TFormModal
        :state="newRunModal as unknown as UseFormModalReturn<unknown>"
        :title="t('newRun')"
        :width="560"
        :translate="t"
        @submit="submitNewRun"
      >
        <template #default="{ formData }">
          <NForm v-if="formData" label-placement="left" label-width="120px">
            <NFormItem :label="t('form.agentId')" required>
              <NInput
                :value="(formData as NewRunForm).agentId"
                :placeholder="t('admin.shared.placeholder.guid')"
                @update:value="(v: string) => ((formData as NewRunForm).agentId = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.versionNumber')">
              <NInputNumber
                :value="(formData as NewRunForm).versionNumber"
                :min="1"
                clearable
                @update:value="(v: number | null) => ((formData as NewRunForm).versionNumber = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.caseInput')" required>
              <NInput
                :value="(formData as NewRunForm).caseInput"
                type="textarea"
                :rows="3"
                @update:value="(v: string) => ((formData as NewRunForm).caseInput = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.caseExpected')">
              <NInput
                :value="(formData as NewRunForm).caseExpected"
                type="textarea"
                :rows="2"
                @update:value="(v: string) => ((formData as NewRunForm).caseExpected = v)"
              />
            </NFormItem>
          </NForm>
        </template>
        <template #footer>
          <div class="t-eval-page__modal-footer">
            <NButton @click="newRunModal.close">{{ t('form.cancel') }}</NButton>
            <NButton
              type="primary"
              :loading="creating"
              :disabled="!newRunFormValid"
              @click="submitNewRun"
            >
              {{ t('form.runNow') }}
            </NButton>
          </div>
        </template>
      </TFormModal>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, ref, watch, onMounted } from 'vue'
import {
  NButton, NSpin, NTag, NForm, NFormItem, NInput, NInputNumber,
} from 'naive-ui'
import { useSafeMessage } from '../../_shared/safeMessage'
import { useFormModal, type UseFormModalReturn } from '../../../headless/useFormModal'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TFormModal from '../../../components/crud/TFormModal.vue'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { translatePageKey } from '../../_shared/translate'
import type {
  CreateEvaluationRunDto,
  EvaluationRunDto,
  EvaluationRunDetailDto,
} from '@tnzi/core/services/ai'

interface NewRunForm {
  agentId: string
  versionNumber: number | null
  caseInput: string
  caseExpected: string
}

const bridge = createAiBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('ai.evaluations', key)

const message = useSafeMessage()

const runs = ref<EvaluationRunDto[]>([])
const listLoading = ref(false)
const leftId = ref<string | null>(null)
const rightId = ref<string | null>(null)
const leftDetail = ref<EvaluationRunDetailDto | null>(null)
const rightDetail = ref<EvaluationRunDetailDto | null>(null)
const creating = ref(false)

const newRunModal = useFormModal<NewRunForm>()

function openNewRunModal(): void {
  newRunModal.open('create', {
    agentId: '',
    versionNumber: null,
    caseInput: '',
    caseExpected: '',
  })
}

const newRunFormValid = computed<boolean>(() => {
  const f = newRunModal.formData.value
  if (!f) return false
  return !!f.agentId && f.caseInput.trim().length > 0
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
  const form = newRunModal.formData.value
  if (!form || !newRunFormValid.value) return
  creating.value = true
  try {
    const dto: CreateEvaluationRunDto = {
      agentId: form.agentId,
      versionNumber: form.versionNumber,
      cases: [
        {
          input: form.caseInput.trim(),
          expectedOutput: form.caseExpected.trim() || null,
        },
      ],
    }
    const created = await bridge.evaluations.create(dto)
    message.success(t('createSuccess'))
    newRunModal.close()
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
.t-eval-page__modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-eval-page__note {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-eval-page__layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
  min-height: 520px;
}
.t-eval-page__list {
  border-right: 1px solid var(--tnzi-border);
  padding-right: 16px;
}
/* Phone: stack the evaluation list above the detail. */
@media (max-width: 767px) {
  .t-eval-page__layout {
    grid-template-columns: 1fr;
    min-height: 0;
  }
  .t-eval-page__list {
    border-right: none;
    padding-right: 0;
    border-bottom: 1px solid var(--tnzi-border);
    padding-bottom: 12px;
    max-height: 36vh;
    overflow: auto;
  }
}
.t-eval-page__list-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 8px;
}
.t-eval-page__hint {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
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
  background: var(--tnzi-layout-bg);
}
.t-eval-page__run-item.is-selected {
  background: rgb(var(--tnzi-primary-rgb) / 0.08);
}
.t-eval-page__run-item.is-left {
  border-color: var(--tnzi-primary);
}
.t-eval-page__run-item.is-right {
  border-color: var(--tnzi-warning);
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
  color: var(--tnzi-success);
}
.t-eval-page__score[data-pass="false"] {
  color: var(--tnzi-error);
}
.t-eval-page__run-meta {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  display: flex;
  justify-content: space-between;
}
.t-eval-page__run-meta code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.t-eval-page__run-time {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}
.t-eval-page__detail {
  padding: 0 4px;
}
.t-eval-page__placeholder,
.t-eval-page__empty {
  color: var(--tnzi-base-text-muted);
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
  border: 1px solid var(--tnzi-border);
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
  border-bottom: 1px solid var(--tnzi-border);
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
  background: var(--tnzi-primary);
}
.t-eval-page__col-label.is-right {
  background: var(--tnzi-warning);
}
.t-eval-page__col-score {
  margin-left: 8px;
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-eval-page__results {
  flex: 1;
  margin: 0;
  font-size: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  background: var(--tnzi-layout-bg);
  padding: 8px;
  border-radius: 4px;
  overflow: auto;
  max-height: 540px;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
