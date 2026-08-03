<template>
  <!--
    Workflows - workflow definition CRUD landing page (Phase J overhaul).

    Replaces the previous "empty editor on the menu landing route" UX. Users
    now see a proper list of workflow definitions with:
      - Create/Edit/Delete (TCrudPage standard)
      - Per-row: Edit graph → /admin/ai/workflows/{id}/edit
      - Per-row: Run → input modal → bridge.workflows.run(id, input)
      - Per-row: Clone (deep-copy via backend)
      - Per-row: Validate (pre-run check: cycles, agent refs)
      - Batch: enable / disable / delete
  -->
  <TCrudPage
    :state="crud"
    :all-columns="workflowColumns"
    :title="title"
    :translate="t"
  >
    <template #primary>
      <NButton v-if="crud.canCreate" type="primary" tertiary size="small" @click="goCreate">
        <template #icon>
          <TSvgIcon icon="mdi:plus" :size="16" />
        </template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <template #batchActions="{ selectedIds }">
      <NPopconfirm v-if="can('ai.workflow.update')" @positive-click="batchEnable(selectedIds as string[])">
        <template #trigger>
          <NButton size="small" type="success" ghost>
            {{ t('actions.batchEnable') }}
          </NButton>
        </template>
        {{ t('actions.confirmBatchEnable') }}
      </NPopconfirm>
      <NPopconfirm v-if="can('ai.workflow.update')" @positive-click="batchDisable(selectedIds as string[])">
        <template #trigger>
          <NButton size="small" type="warning" ghost>
            {{ t('actions.batchDisable') }}
          </NButton>
        </template>
        {{ t('actions.confirmBatchDisable') }}
      </NPopconfirm>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="workflowFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>

    <template #rowActions="{ row }">
      <TRowActions
        :row="row as Row"
        :actions="rowActions"
        :translate="t"
      >
        <template #prepend="{ row: r }">
          <NButton v-if="can('ai.workflow.update')" size="small" type="primary" ghost @click="goEdit(r as Row)">
            {{ t('actions.editGraph') }}
          </NButton>
        </template>
        <template #middle="{ row: r }">
          <NDropdown
            trigger="click"
            :options="rowMenuOptions(r as Row)"
            @select="(key) => handleRowMenu(String(key), r as Row)"
          >
            <NButton size="small" ghost>
              {{ t('actions.more') }} ▾
            </NButton>
          </NDropdown>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>

  <!-- Run modal: collect a single text input and POST to /workflows/{id}/run.
       The page intentionally stays on synchronous run (not SSE stream) -
       streaming-trace UI lives on the WorkflowRunViewer page where the user
       can also Resume / Approve / Reject. -->
  <TOverlayTheme>
  <NModal v-model:show="runModal.show" :title="runModalTitle" preset="card" size="small" class="max-w-560px">
    <NForm label-placement="left" label-width="100px">
      <NFormItem :label="t('run.input')" required>
        <NInput
          v-model:value="runModal.input"
          type="textarea"
          :rows="6"
          :placeholder="t('run.inputPlaceholder')"
        />
      </NFormItem>
      <NFormItem :label="t('run.userId')">
        <NInput
          v-model:value="runModal.userId"
          :placeholder="t('run.userIdPlaceholder')"
        />
      </NFormItem>
    </NForm>
    <template #footer>
      <div class="flex justify-end gap-8px">
        <NButton @click="runModal.show = false">{{ t('admin.common.cancel') }}</NButton>
        <NButton
          type="primary"
          :loading="runModal.running"
          :disabled="!runModal.input?.trim()"
          @click="confirmRun"
        >
          {{ t('actions.run') }}
        </NButton>
      </div>
    </template>
  </NModal>
  </TOverlayTheme>

  <!-- Validate result modal -->
  <TOverlayTheme>
  <NModal v-model:show="validateModal.show" :title="t('validate.title')" preset="card" size="small" class="max-w-520px">
    <div v-if="validateModal.loading" class="t-wf-list__validate-loading">
      <NSpin />
    </div>
    <div v-else-if="validateModal.result">
      <NAlert
        :type="validateModal.result.isValid ? 'success' : 'error'"
        :title="validateModal.result.isValid ? t('validate.passed') : t('validate.failed')"
        :show-icon="true"
      >
        <ul v-if="(validateModal.result.errors?.length ?? 0) > 0" class="t-wf-list__validate-list">
          <li v-for="(err, i) in (validateModal.result.errors ?? [])" :key="`e-${i}`">
            {{ err }}
          </li>
        </ul>
        <ul v-if="(validateModal.result.warnings?.length ?? 0) > 0" class="t-wf-list__validate-list">
          <li v-for="(w, i) in (validateModal.result.warnings ?? [])" :key="`w-${i}`">
            ⚠ {{ w }}
          </li>
        </ul>
        <div v-if="validateModal.result.isValid && (validateModal.result.errors?.length ?? 0) === 0 && (validateModal.result.warnings?.length ?? 0) === 0">
          {{ t('validate.cleanRun') }}
        </div>
      </NAlert>
    </div>
    <template #footer>
      <div class="flex justify-end">
        <NButton @click="validateModal.show = false">{{ t('admin.common.close') }}</NButton>
      </div>
    </template>
  </NModal>
  </TOverlayTheme>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { h } from 'vue'
import {
  NButton,
  NPopconfirm,
  NModal,
  NForm,
  NFormItem,
  NInput,
  NSpin,
  NAlert,
  NDropdown,
  useDialog,
  type DropdownOption,
} from 'naive-ui'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TRowActions from '../../../components/crud/TRowActions.vue'
import { TOverlayTheme } from '../../../components/overlay'
import { TSvgIcon } from '@tnzi/ui'
import { useCrudPage } from '../../../headless/useCrudPage'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { type RowAction } from '../../../headless/row-actions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safe-message'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { interpolate, translatePageKey } from '../../_shared/translate'
import { workflowColumns, workflowFormSchema } from './workflow-config'
import type {
  WorkflowDefinitionDto,
  CreateWorkflowDefinitionDto,
  UpdateWorkflowDefinitionDto,
  WorkflowValidationResultDto,
} from '@tnzi/core/services/ai'
// 0.2.72+ (B4): enum value routed through ai-bridge so the page stays
// clean under the `no-restricted-imports` guard.
import { WorkflowExecutionMode } from '../../../services/bridges/ai-bridge'

type Row = WorkflowDefinitionDto

const title = 'title'
const router = useRouter()
const bridge = createAiBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()
// ui-admin shells don't always wrap with NMessage/NDialog providers (and unit
// tests mount bare), so guard both - `useSafeMessage` no-ops without a provider,
// and `useDialog` is wrapped so a missing provider falls back to native confirm.
const message = useSafeMessage()
const dialog = (() => {
  try {
    return useDialog()
  } catch {
    return null
  }
})()

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.workflows', key), params)

const crud = useCrudPage<WorkflowDefinitionDto>({
  pageId: 'ai.workflows',
  permission: 'ai.workflow',
  columns: workflowColumns,
  rowKey: (r) => r.id,
  fetchData: (q) => bridge.workflows.fetch(q),
  // CreateWorkflowDefinitionDto requires `steps[]` - we default to an empty
  // array so users can scaffold a definition from the list and then add
  // steps in the dedicated editor (graph + JSON modes).
  createData: async (data) => {
    const partial = data as Partial<WorkflowDefinitionDto>
    // executionMode is a string enum whose values are the member names the
    // backend serializes/accepts. The form select binds one of those names;
    // normalise a legacy numeric value defensively, else default to Sequential.
    const modeRaw = partial.executionMode as unknown
    let mode: WorkflowExecutionMode = WorkflowExecutionMode.Sequential
    if (typeof modeRaw === 'string' && modeRaw in WorkflowExecutionMode) {
      mode = modeRaw as WorkflowExecutionMode
    } else if (typeof modeRaw === 'number') {
      mode = ([
        WorkflowExecutionMode.Sequential,
        WorkflowExecutionMode.Parallel,
        WorkflowExecutionMode.Dag,
      ][modeRaw]) ?? WorkflowExecutionMode.Sequential
    }
    const payload: CreateWorkflowDefinitionDto = {
      name: String(partial.name ?? '').trim(),
      description: partial.description ?? null,
      executionMode: mode,
      isEnabled: Boolean(partial.isEnabled ?? true),
      steps: (partial.steps as never) ?? [],
    }
    try {
      const created = await bridge.workflows.create(payload)
      message.success(t('actions.createSuccess'))
      // Auto-navigate to the editor so the user can immediately add steps.
      router.push({ name: 'ai.workflows.editor', params: { id: created.id } }).catch(() => undefined)
      return created
    } catch (err) {
      // Surface the backend error so the modal can't silently swallow it.
      const msg = err instanceof Error ? err.message : t('actions.createError')
      message.error(msg)
      throw err
    }
  },
  updateData: (id, data) => bridge.workflows.update(String(id), data as UpdateWorkflowDefinitionDto),
  deleteData: (ids) => bridge.workflows.delete(ids.map(String)),
})

// Edit graph + run/clone/validate/delete are rendered by the page itself in the
// #prepend ("Edit graph") and #middle ("More▾") slots, so the inner
// <TRowActions> has no built-in inline actions of its own.
const rowActions: RowAction<Row>[] = []

function goCreate(): void {
  crud.openCreate()
}

function goEdit(row: Row): void {
  router.push({ name: 'ai.workflows.editor', params: { id: row.id } }).catch(() => undefined)
}

// --- Row "更多" dropdown ---------------------------------------------------
// Collapses run / clone / validate / delete into a single dropdown so the
// row action column stays narrow regardless of feature growth.
function rowMenuOptions(row: Row): DropdownOption[] {
  const options: DropdownOption[] = []
  if (can('ai.workflow.execute')) {
    options.push({
      key: 'run',
      label: t('actions.run'),
      disabled: !row.isEnabled,
      icon: () => h(TSvgIcon, { icon: 'mdi:play', size: 16 }),
    })
  }
  if (can('ai.workflow.create')) {
    options.push({
      key: 'clone',
      label: t('actions.clone'),
      icon: () => h(TSvgIcon, { icon: 'mdi:content-copy', size: 16 }),
    })
  }
  // Validate is a read-only pre-run check - always available.
  options.push({
    key: 'validate',
    label: t('actions.validate'),
    icon: () => h(TSvgIcon, { icon: 'mdi:check-circle-outline', size: 16 }),
  })
  if (crud.canDelete) {
    options.push({
      key: 'delete',
      label: t('admin.crud.delete'),
      icon: () => h(TSvgIcon, { icon: 'mdi:trash-can-outline', size: 16 }),
    })
  }
  return options
}

function handleRowMenu(key: string, row: Row): void {
  if (key === 'run') openRunModal(row)
  else if (key === 'clone') void cloneWorkflow(row.id)
  else if (key === 'validate') void validateWorkflow(row.id)
  else if (key === 'delete') confirmDelete(row)
}

function confirmDelete(row: Row): void {
  if (!dialog) {
    // No dialog provider (bare mount / test harness) - fall back to the native
    // confirm so the confirmation gate is never silently skipped.
    if (typeof window !== 'undefined' && window.confirm(t('admin.crud.confirmDelete'))) {
      void crud.handleDelete([row.id])
    }
    return
  }
  dialog.warning({
    title: t('admin.crud.delete'),
    content: t('admin.crud.confirmDelete'),
    positiveText: t('admin.common.confirm'),
    negativeText: t('admin.common.cancel'),
    onPositiveClick: async () => {
      await crud.handleDelete([row.id])
    },
  })
}

// --- Run modal --------------------------------------------------------------
const runModal = reactive<{
  show: boolean
  running: boolean
  workflow: Row | null
  input: string
  userId: string
}>({
  show: false,
  running: false,
  workflow: null,
  input: '',
  userId: '',
})

const runModalTitle = computed(() =>
  runModal.workflow ? `${t('actions.run')} - ${runModal.workflow.name}` : t('actions.run'),
)

function openRunModal(row: Row): void {
  runModal.workflow = row
  runModal.input = ''
  runModal.userId = ''
  runModal.show = true
}

async function confirmRun(): Promise<void> {
  const wf = runModal.workflow
  if (!wf) return
  runModal.running = true
  try {
    const result = await bridge.workflows.run(wf.id, runModal.input.trim(), runModal.userId.trim() || undefined)
    // The backend wraps every response in `ApiResult` (HTTP 200 even on
    // business-level failure) and the bridge unwraps `.data`. When the
    // service returns `Fail<>(...)`, `data` is null - surface that as a
    // toast instead of pretending it succeeded.
    if (!result) {
      message.error(t('run.failed'))
      return
    }
    const status = String(result.status ?? '').toLowerCase()
    if (status === 'failed' || status === 'error') {
      // The service returns the failure message on result.output (free-form).
      message.error(result.output || t('run.failed'))
      return
    }
    runModal.show = false
    message.success(t('run.queued', { id: String(result.executionId ?? result.runId ?? '?') }))
    if (result.executionId) {
      router
        .push({ name: 'ai.workflowRuns', query: { runId: result.executionId } })
        .catch(() => undefined)
    }
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('run.failed'))
  } finally {
    runModal.running = false
  }
}

// --- Clone ------------------------------------------------------------------
async function cloneWorkflow(id: string): Promise<void> {
  try {
    const clone = await bridge.workflows.clone(id)
    message.success(t('actions.cloneSuccess'))
    await crud.refresh()
    // Auto-navigate to the new copy's editor so the user can immediately tweak.
    router.push({ name: 'ai.workflows.editor', params: { id: clone.id } }).catch(() => undefined)
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.cloneError'))
  }
}

// --- Validate ---------------------------------------------------------------
const validateModal = reactive<{
  show: boolean
  loading: boolean
  result: WorkflowValidationResultDto | null
}>({ show: false, loading: false, result: null })

async function validateWorkflow(id: string): Promise<void> {
  validateModal.show = true
  validateModal.loading = true
  validateModal.result = null
  try {
    validateModal.result = await bridge.workflows.validate(id)
  } catch (err) {
    validateModal.result = {
      isValid: false,
      errors: [err instanceof Error ? err.message : String(err)],
      warnings: [],
    }
  } finally {
    validateModal.loading = false
  }
}

// --- Batch enable / disable -------------------------------------------------
async function batchEnable(ids: string[]): Promise<void> {
  if (ids.length === 0) return
  try {
    const n = await bridge.workflows.batchEnable(ids)
    message.success(t('actions.batchEnableSuccess', { n }))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.batchEnableError'))
  }
}

async function batchDisable(ids: string[]): Promise<void> {
  if (ids.length === 0) return
  try {
    const n = await bridge.workflows.batchDisable(ids)
    message.success(t('actions.batchDisableSuccess', { n }))
    crud.batchActions.clear()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.batchDisableError'))
  }
}

defineExpose({ crud })
</script>

<style scoped>
.t-wf-list__validate-loading {
  display: flex;
  justify-content: center;
  padding: 24px;
}
.t-wf-list__validate-list {
  margin: 8px 0 0;
  padding-left: 16px;
  font-size: 13px;
}
</style>
