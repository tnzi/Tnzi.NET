<template>
  <!--
    Version history of one workflow definition.

    The backend snapshots the definition before every update and before every
    restore, so the history reads newest-first as "what the workflow looked
    like before change N". Two overlays, each with its own deep-link key:
      ?history=view:<workflowId>   the history drawer (list)
      ?version=view:<versionNumber> one snapshot (read-only) with a restore action
  -->
  <TDetailHost
    :state="historyDetail"
    :title="t('versions.title')"
    :width="640"
    :footer="false"
    :translate="t"
  >
    <template #default>
      <div class="t-wf-versions" data-test="wf-versions">
        <div class="t-wf-versions__toolbar">
          <span class="t-wf-versions__hint">{{ t('versions.hint') }}</span>
          <NButton size="small" tertiary :loading="listLoading" @click="loadVersions">
            <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
            {{ t('versions.refresh') }}
          </NButton>
        </div>
        <NAlert v-if="listError" type="error" :title="t('versions.loadError')" data-test="wf-versions-error">
          {{ listError }}
        </NAlert>
        <TResponsiveTable
          v-else
          :columns="columns"
          :data="versions"
          :loading="listLoading"
          :row-key="(row: WorkflowDefinitionVersionDto) => row.versionNumber"
          :pagination="false"
          :empty-text="t('versions.empty')"
          :row-actions="rowActions"
          :row-actions-title="t('versions.columns.actions')"
          :translate="t"
          size="small"
        />
      </div>
    </template>
  </TDetailHost>

  <TDetailHost
    :state="versionDetail"
    :title="viewed ? t('versions.viewTitle', { n: viewed.versionNumber }) : t('versions.title')"
    :width="720"
    :translate="t"
  >
    <template #default>
      <NSpin :show="viewedLoading">
        <NAlert v-if="viewedError" type="error" :title="t('versions.loadError')" data-test="wf-version-error">
          {{ viewedError }}
        </NAlert>
        <div v-else-if="viewed" class="t-wf-versions__snapshot" data-test="wf-version-snapshot">
          <div class="t-wf-versions__meta">
            <div><span>{{ t('versions.columns.createdAt') }}:</span> {{ formatDateTime(viewed.creationTime) }}</div>
            <div><span>{{ t('versions.columns.note') }}:</span> {{ viewed.changeDescription || t('versions.noNote') }}</div>
            <template v-if="snapshot.parsed">
              <div><span>{{ t('form.name') }}:</span> {{ snapshot.name ?? EMPTY_DASH }}</div>
              <div><span>{{ t('form.executionMode') }}:</span> {{ snapshot.executionMode ?? EMPTY_DASH }}</div>
              <div>
                <span>{{ t('form.isEnabled') }}:</span>
                {{ snapshot.isEnabled === undefined ? EMPTY_DASH : snapshot.isEnabled ? t('admin.shared.status.enabled') : t('admin.shared.status.disabled') }}
              </div>
              <div><span>{{ t('editor.stepsCount') }}:</span> {{ snapshot.stepCount ?? EMPTY_DASH }}</div>
            </template>
          </div>
          <NAlert v-if="!snapshot.parsed" type="warning" :show-icon="false" class="mb-8px">
            {{ t('versions.unreadable') }}
          </NAlert>
          <pre class="t-wf-versions__json">{{ snapshot.pretty }}</pre>
        </div>
      </NSpin>
    </template>
    <template #footer="{ close }">
      <div class="flex justify-end gap-8px">
        <NButton size="small" @click="close">{{ t('admin.common.close') }}</NButton>
        <NPopconfirm v-if="canRestore && viewed && !viewedError" @positive-click="restoreViewed">
          <template #trigger>
            <NButton size="small" type="warning" :loading="restoring !== null" data-test="wf-version-restore">
              <template #icon><TSvgIcon icon="mdi:restore" :size="16" /></template>
              {{ t('versions.restore') }}
            </NButton>
          </template>
          {{ restoreConfirmText(viewed.versionNumber) }}
        </NPopconfirm>
      </div>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import { NAlert, NButton, NPopconfirm, NSpin, NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { useDetail } from '../../../headless/useDetail'
import type { RowAction } from '../../../headless/row-actions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safe-message'
import { makePageTranslator } from '../../_shared/translate'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { summarizeVersionSnapshot } from './workflow-versions'
import type { WorkflowDefinitionVersionDto } from '@tnzi/core/services/ai'

const props = defineProps<{
  /** The workflow whose history is shown. */
  workflowId?: string
  /** Hold `ai.workflow.update`: restore is hidden otherwise (the backend enforces it too). */
  canRestore: boolean
  /** The editor has unsaved changes a restore would discard. */
  dirty: boolean
}>()

const emit = defineEmits<{
  /** A version was restored; the definition on the server changed. */
  restored: [versionNumber: number]
}>()

const t = makePageTranslator('ai.workflows')
const bridge = createAiBridge({ client: useAdminClient() })
const message = useSafeMessage()

// --- History drawer (?history=view:<workflowId>) -----------------------------
interface HistoryTarget { id: string }

const historyDetail = useDetail<HistoryTarget>({
  mode: 'drawer',
  url: 'history',
  // A shared link names the workflow; only this editor's workflow resolves,
  // anything else is a dangling key and is dropped.
  loadData: async (id) => (props.workflowId && String(id) === props.workflowId ? { id: props.workflowId } : null),
})

const versions = ref<WorkflowDefinitionVersionDto[]>([])
const listLoading = ref(false)
const listError = ref('')

async function loadVersions(): Promise<void> {
  const id = props.workflowId
  if (!id) return
  listLoading.value = true
  listError.value = ''
  try {
    versions.value = await bridge.workflows.getVersions(id)
  } catch (err) {
    versions.value = []
    listError.value = err instanceof Error ? err.message : String(err)
  } finally {
    listLoading.value = false
  }
}

watch(() => historyDetail.data.value, (target) => {
  if (target) void loadVersions()
})

function open(): void {
  if (!props.workflowId) return
  void historyDetail.open('view', { id: props.workflowId })
}

defineExpose({ open })

// --- One version (?version=view:<versionNumber>) ------------------------------
const versionDetail = useDetail<WorkflowDefinitionVersionDto>({
  mode: 'modal',
  url: 'version',
  getId: (v) => v.versionNumber,
  loadData: async (n) => {
    const id = props.workflowId
    const versionNumber = Number(n)
    if (!id || !Number.isInteger(versionNumber)) return null
    return bridge.workflows.getVersion(id, versionNumber)
  },
})

const viewed = ref<WorkflowDefinitionVersionDto | null>(null)
const viewedLoading = ref(false)
const viewedError = ref('')
const snapshot = computed(() => summarizeVersionSnapshot(viewed.value?.definition))

// The history list carries no definition body; the snapshot is fetched per
// version. A deep link arrives through `loadData` already complete.
watch(() => versionDetail.data.value, async (v) => {
  viewedError.value = ''
  viewed.value = v
  if (!v || v.definition != null || !props.workflowId) return
  viewedLoading.value = true
  try {
    const full = await bridge.workflows.getVersion(props.workflowId, v.versionNumber)
    if (versionDetail.data.value?.versionNumber === v.versionNumber) viewed.value = full
  } catch (err) {
    viewedError.value = err instanceof Error ? err.message : String(err)
  } finally {
    viewedLoading.value = false
  }
})

// --- Restore ------------------------------------------------------------------
const restoring = ref<number | null>(null)

function restoreConfirmText(versionNumber: number): string {
  const base = t('versions.restoreConfirm', { n: versionNumber })
  return props.dirty ? `${base} ${t('versions.restoreDiscardsDraft')}` : base
}

async function restore(versionNumber: number): Promise<void> {
  const id = props.workflowId
  if (!id || !props.canRestore) return
  restoring.value = versionNumber
  try {
    await bridge.workflows.restoreVersion(id, versionNumber)
    message.success(t('versions.restored', { n: versionNumber }))
    versionDetail.close()
    emit('restored', versionNumber)
    await loadVersions()
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('versions.restoreError'))
  } finally {
    restoring.value = null
  }
}

function restoreViewed(): void {
  if (viewed.value) void restore(viewed.value.versionNumber)
}

// --- Table --------------------------------------------------------------------
const columns = computed<DataTableColumns<WorkflowDefinitionVersionDto>>(() => [
  {
    key: 'versionNumber',
    title: t('versions.columns.version'),
    width: 90,
    render: (row) => h(NTag, { size: 'small', type: 'info', bordered: false }, () => `v${row.versionNumber}`),
  },
  {
    key: 'changeDescription',
    title: t('versions.columns.note'),
    render: (row) => row.changeDescription || t('versions.noNote'),
  },
  {
    key: 'creationTime',
    title: t('versions.columns.createdAt'),
    width: 170,
    render: (row) => formatDateTime(row.creationTime),
  },
])

const rowActions: RowAction<WorkflowDefinitionVersionDto>[] = [
  { key: 'view', onClick: (row) => versionDetail.open('view', row) },
  {
    key: 'restore',
    label: 'versions.restore',
    type: 'warning',
    show: () => props.canRestore,
    disabled: () => restoring.value !== null,
    confirm: (row) => restoreConfirmText(row.versionNumber),
    onClick: (row) => restore(row.versionNumber),
  },
]
</script>

<style scoped>
.t-wf-versions {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-wf-versions__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-wf-versions__hint {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-wf-versions__meta {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 4px 16px;
  margin-bottom: 12px;
  font-size: 13px;
}
.t-wf-versions__meta span {
  color: var(--tnzi-base-text-muted);
  margin-right: 6px;
}
.t-wf-versions__json {
  margin: 0;
  padding: 8px;
  background: var(--tnzi-layout-bg);
  border-radius: 4px;
  font-size: 12px;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 50vh;
  overflow: auto;
}
</style>
