<template>
  <!--
    WorkflowEditor — heavyweight visual editor.

    Layout:
      - Top header:     TDetailLayout plain — title (workflow name) + back + action buttons
      - View tabs:      visual / JSON toggle rendered inside body (below header)
      - Left panel:     node palette ("add node by type") + node list (select/delete/duplicate)
      - Center canvas:  Vue Flow with drag-to-move + drag-to-connect.
                        Node positions persist via configuration["__x"/"__y"].
                        Connections become DependsOn entries on the target step.
      - Right panel:    EITHER node property form (when a node is selected)
                        OR workflow metadata + stats (when nothing is selected).

    The JSON view stays available as an advanced fall-through tab, but the
    visual editor is now the primary path — `addStep` builds typed templates
    and the property form mutates draft.steps in place so the round-trip
    matches what the backend executors expect.
  -->
  <TDetailLayout
    layout="plain"
    :title="workflow?.name || t('editor.untitled')"
    :back="'/admin/ai/workflows'"
    :translate="t"
  >
    <template #title>
      <div class="t-wf-editor__header-title">
        <span class="t-wf-editor__header-name">{{ workflow?.name || t('editor.untitled') }}</span>
        <NTag v-if="workflow" size="small" :type="workflow.isEnabled ? 'success' : 'warning'" :bordered="false">
          {{ workflow.isEnabled ? t('admin.shared.status.enabled') : t('admin.shared.status.disabled') }}
        </NTag>
      </div>
    </template>

    <template #actions>
      <NButton size="small" tertiary :disabled="!workflow" @click="onValidate">
        <template #icon><TSvgIcon icon="mdi:shield-check-outline" :size="16" /></template>
        {{ t('actions.validate') }}
      </NButton>
      <NButton size="small" type="info" :disabled="!workflow?.isEnabled" @click="openRun">
        <template #icon><TSvgIcon icon="mdi:play" :size="16" /></template>
        {{ t('actions.run') }}
      </NButton>
      <NButton
        size="small"
        :type="workflow?.isEnabled ? 'warning' : 'success'"
        :disabled="!workflow"
        @click="togglePublish"
      >
        {{ workflow?.isEnabled ? t('actions.disable') : t('actions.enable') }}
      </NButton>
      <NButton size="small" type="primary" :loading="saving" :disabled="!dirty" @click="onSave">
        <template #icon><TSvgIcon icon="mdi:content-save-outline" :size="16" /></template>
        {{ t('admin.common.save') }}
      </NButton>
    </template>

    <template #default>
      <div class="t-wf-editor">
        <!-- View-mode tabs (visual / JSON) — rendered inside body so they stay with the content -->
        <div class="t-wf-editor__view-bar">
          <NTabs
            v-model:value="viewMode"
            type="segment"
            size="small"
            animated
            class="t-wf-editor__view-tabs"
          >
            <NTab name="visual" :tab="t('editor.visualTab')" />
            <NTab name="json" :tab="t('editor.jsonTab')" />
          </NTabs>
        </div>

        <div v-if="loading" class="t-wf-editor__loading">
          <NSpin />
        </div>
        <NAlert v-else-if="loadError" type="error" :title="t('editor.loadError')">{{ loadError }}</NAlert>
        <div v-else-if="!workflowId" class="t-wf-editor__empty">{{ t('editor.missingId') }}</div>

        <!-- Visual editor (default) -->
        <div v-else-if="viewMode === 'visual'" class="t-wf-editor__body">
          <!-- Left: node palette + node list -->
          <aside class="t-wf-editor__sidebar">
        <NCard size="small" :title="t('editor.nodePalette')" :bordered="false">
          <div class="t-wf-editor__palette">
            <button
              v-for="meta in nodeTypeMetas"
              :key="meta.kind"
              type="button"
              class="t-wf-editor__palette-btn"
              :style="{ borderColor: meta.color }"
              @click="addStepOfKind(meta.kind)"
            >
              <TSvgIcon :icon="meta.icon" :size="16" :style="{ color: meta.color }" />
              <span>{{ t(`nodeTypes.${meta.labelKey}`) }}</span>
            </button>
          </div>
        </NCard>

        <NCard
          size="small"
          :title="t('editor.nodeList')"
          :bordered="false"
          class="t-wf-editor__node-list-card mt-12px flex-1 min-h-0 flex flex-col"
        >
          <template #header-extra>
            <span class="t-wf-editor__stats-pill">{{ draft.steps?.length ?? 0 }}</span>
          </template>
          <ul v-if="draft.steps?.length" class="t-wf-editor__node-list">
            <li
              v-for="step in draft.steps"
              :key="step.stepId ?? ''"
              class="t-wf-editor__node-row"
              :class="{ 'is-active': step.stepId === selectedStepId }"
              @click="selectStep(step.stepId)"
            >
              <TSvgIcon
                :icon="getNodeTypeMeta(getStepKind(step)).icon"
                :size="14"
                :style="{ color: getNodeTypeMeta(getStepKind(step)).color }"
              />
              <span class="t-wf-editor__node-row-label">{{ step.stepId }}</span>
              <NButton size="tiny" text @click.stop="duplicateStep(step)">
                <template #icon><TSvgIcon icon="mdi:content-duplicate" :size="14" /></template>
              </NButton>
              <NButton size="tiny" text type="error" @click.stop="deleteStep(step.stepId)">
                <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
              </NButton>
            </li>
          </ul>
          <div v-else class="t-wf-editor__node-list-empty">{{ t('editor.nodeListEmpty') }}</div>
        </NCard>
      </aside>

      <!-- Center: canvas -->
      <section class="t-wf-editor__canvas">
        <NCard :bordered="false" class="t-wf-editor__canvas-card">
          <template #header>
            <div class="t-wf-editor__canvas-header">
              <span>{{ t('editor.graph') }}</span>
              <span class="t-wf-editor__canvas-hint">{{ t('editor.canvasHint') }}</span>
            </div>
          </template>
          <Suspense>
            <WorkflowCanvas
              :nodes="vfNodes"
              :edges="vfEdges"
              @node-click="onCanvasNodeClick"
              @nodes-change="onCanvasNodesChange"
              @edges-change="onCanvasEdgesChange"
              @connect="onCanvasConnect"
            >
              <!--
                Custom default-node template — mirrors the palette: an mdi
                icon tinted with the node-type color sits to the left of the
                step id label. `Handle` (re-exported from @vue-flow/core via
                @tnzi/ui-ai) provides the drag-to-connect source/target dots.
              -->
              <template #node-default="nodeProps: NodeProps">
                <Handle type="target" :position="Position.Left" />
                <div
                  class="t-wf-flow-node-content"
                  :title="(nodeProps.data?.label as string) ?? nodeProps.id"
                >
                  <TSvgIcon
                    class="t-wf-flow-node-icon"
                    :icon="(nodeProps.data?.icon as string) ?? 'mdi:circle-outline'"
                    :size="14"
                    :style="{ color: (nodeProps.data?.color as string) ?? '#999' }"
                  />
                  <span class="t-wf-flow-node-label">
                    {{ (nodeProps.data?.label as string) ?? nodeProps.id }}
                  </span>
                </div>
                <Handle type="source" :position="Position.Right" />
              </template>
            </WorkflowCanvas>
            <template #fallback>
              <div class="t-wf-editor__placeholder">{{ t('editor.canvasLoading') }}</div>
            </template>
          </Suspense>
        </NCard>
      </section>

      <!-- Right: node properties OR workflow metadata -->
      <aside class="t-wf-editor__inspector">
        <NCard
          v-if="selectedStep"
          size="small"
          :bordered="false"
          class="t-wf-editor__inspector-card"
        >
          <template #header>
            <div class="t-wf-editor__inspector-header">
              <TSvgIcon
                :icon="getNodeTypeMeta(selectedKind).icon"
                :size="16"
                :style="{ color: getNodeTypeMeta(selectedKind).color }"
              />
              <span>{{ t(`nodeTypes.${getNodeTypeMeta(selectedKind).labelKey}`) }}</span>
            </div>
          </template>

          <NForm label-placement="top" size="small" :show-feedback="false">
            <NFormItem :label="t('nodeForm.stepId')">
              <NInput
                :value="selectedStep.stepId"
                @update:value="(v: string) => onStepIdInput(v)"
              />
            </NFormItem>

            <NFormItem :label="t('nodeForm.nodeType')">
              <NSelect
                :value="selectedKind"
                :options="nodeTypeSelectOptions"
                @update:value="(v: string) => changeKind(v)"
              />
            </NFormItem>

            <NFormItem
              v-if="selectedFields.agent"
              :label="t('nodeForm.agent')"
            >
              <NSelect
                v-model:value="selectedStep.agentId"
                :options="agentOptions"
                :loading="agentsLoading"
                clearable
                filterable
                :placeholder="t('nodeForm.agentPlaceholder')"
                @update:value="markDirty"
              />
            </NFormItem>

            <template v-if="selectedFields.providerModel">
              <NFormItem :label="t('nodeForm.provider')">
                <NInput v-model:value="selectedStep.provider" @update:value="markDirty" />
              </NFormItem>
              <NFormItem :label="t('nodeForm.model')">
                <NInput v-model:value="selectedStep.model" @update:value="markDirty" />
              </NFormItem>
            </template>

            <NFormItem v-if="selectedFields.instructions" :label="t('nodeForm.instructions')">
              <NInput
                v-model:value="selectedStep.instructions"
                type="textarea"
                :rows="4"
                :placeholder="t('nodeForm.instructionsPlaceholder')"
                @update:value="markDirty"
              />
            </NFormItem>

            <NFormItem v-if="selectedFields.condition" :label="t('nodeForm.condition')">
              <NInput
                v-model:value="selectedStep.condition"
                :placeholder="t('nodeForm.conditionPlaceholder')"
                @update:value="markDirty"
              />
            </NFormItem>

            <NFormItem :label="t('nodeForm.dependsOn')">
              <NSelect
                v-model:value="selectedStep.dependsOn"
                multiple
                filterable
                clearable
                :options="dependsOnOptions"
                :placeholder="t('nodeForm.dependsOnPlaceholder')"
                @update:value="markDirty"
              />
            </NFormItem>

            <div v-if="selectedFields.retry || selectedFields.timeout" class="t-wf-editor__row">
              <NFormItem v-if="selectedFields.retry" :label="t('nodeForm.maxRetries')">
                <NInputNumber
                  v-model:value="selectedStep.maxRetries"
                  :min="0"
                  :max="10"
                  @update:value="markDirty"
                />
              </NFormItem>
              <NFormItem v-if="selectedFields.retry" :label="t('nodeForm.retryDelay')">
                <NInputNumber
                  v-model:value="selectedStep.retryDelaySeconds"
                  :min="1"
                  :max="300"
                  @update:value="markDirty"
                />
              </NFormItem>
              <NFormItem v-if="selectedFields.timeout" :label="t('nodeForm.timeout')">
                <NInputNumber
                  v-model:value="selectedStep.timeoutSeconds"
                  :min="1"
                  :max="3600"
                  clearable
                  @update:value="markDirty"
                />
              </NFormItem>
            </div>

            <NFormItem v-if="selectedFields.requiresApproval">
              <NCheckbox
                v-model:checked="selectedStep.requiresApproval"
                @update:checked="markDirty"
              >
                {{ t('nodeForm.requiresApproval') }}
              </NCheckbox>
            </NFormItem>
          </NForm>

          <!-- !important beats Naive's own .n-divider:not(.n-divider--vertical) margin (specificity 0,2,0) -->
          <NDivider class="!m-[12px_0_8px]" />
          <div class="t-wf-editor__inspector-footer">
            <NButton size="tiny" tertiary @click="duplicateStep(selectedStep)">
              <template #icon><TSvgIcon icon="mdi:content-duplicate" :size="14" /></template>
              {{ t('editor.duplicate') }}
            </NButton>
            <NButton size="tiny" tertiary type="error" @click="deleteStep(selectedStep.stepId)">
              <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
              {{ t('admin.crud.delete') }}
            </NButton>
          </div>
        </NCard>

        <NCard
          v-else
          size="small"
          :title="t('editor.metadata')"
          :bordered="false"
          class="t-wf-editor__inspector-card"
        >
          <NForm label-placement="top" size="small" :show-feedback="false">
            <NFormItem :label="t('form.name')">
              <NInput v-model:value="draft.name" :placeholder="t('form.name')" @update:value="markDirty" />
            </NFormItem>
            <NFormItem :label="t('form.description')">
              <NInput
                v-model:value="draft.description"
                type="textarea"
                :rows="3"
                :placeholder="t('form.description')"
                @update:value="markDirty"
              />
            </NFormItem>
            <NFormItem :label="t('form.executionMode')">
              <NSelect
                v-model:value="draft.executionMode"
                :options="executionModeOptions"
                @update:value="markDirty"
              />
            </NFormItem>
            <NFormItem>
              <NCheckbox v-model:checked="draft.isEnabled" @update:checked="markDirty">
                {{ t('form.isEnabled') }}
              </NCheckbox>
            </NFormItem>
          </NForm>

          <!-- !important beats Naive's own .n-divider margin (specificity 0,2,0) -->
          <NDivider class="!m-[12px_0]" />
          <div class="t-wf-editor__stats">
            <div><span>{{ t('editor.stepsCount') }}:</span> {{ draft.steps?.length ?? 0 }}</div>
            <div><span>{{ t('columns.executionMode') }}:</span> {{ executionModeLabel }}</div>
          </div>
        </NCard>
      </aside>
    </div>

    <!-- JSON editor (advanced fall-through) -->
    <div v-else class="t-wf-editor__json-body">
      <NCard size="small" :title="t('editor.stepsJson')" :bordered="false" class="t-wf-editor__steps-card">
        <template #header-extra>
          <NButton size="tiny" tertiary :disabled="!stepsJsonDirty" @click="formatStepsJson">
            <template #icon><TSvgIcon icon="mdi:code-braces" :size="14" /></template>
            {{ t('editor.format') }}
          </NButton>
        </template>
        <NAlert v-if="stepsJsonError" type="error" :show-icon="false" class="mb-8px">
          {{ stepsJsonError }}
        </NAlert>
        <NInput
          v-model:value="stepsJson"
          type="textarea"
          :rows="28"
          class="t-wf-editor__steps-input"
          :placeholder="t('editor.stepsPlaceholder')"
          @update:value="onStepsJsonChange"
        />
      </NCard>
    </div>

        <!-- Run modal -->
        <NModal v-model:show="runModal.show" :title="t('actions.run')" preset="card" class="max-w-560px">
          <NForm label-placement="left" label-width="100px">
            <NFormItem :label="t('run.input')" required>
              <NInput v-model:value="runModal.input" type="textarea" :rows="6" :placeholder="t('run.inputPlaceholder')" />
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

        <!-- Validate result modal -->
        <NModal v-model:show="validateModal.show" :title="t('validate.title')" preset="card" class="max-w-520px">
          <NAlert
            v-if="validateModal.result"
            :type="validateModal.result.isValid ? 'success' : 'error'"
            :title="validateModal.result.isValid ? t('validate.passed') : t('validate.failed')"
            :show-icon="true"
          >
            <ul v-if="(validateModal.result.errors?.length ?? 0) > 0" class="t-wf-editor__validate-list">
              <li v-for="(err, i) in (validateModal.result.errors ?? [])" :key="`e-${i}`">{{ err }}</li>
            </ul>
            <ul v-if="(validateModal.result.warnings?.length ?? 0) > 0" class="t-wf-editor__validate-list">
              <li v-for="(w, i) in (validateModal.result.warnings ?? [])" :key="`w-${i}`">⚠ {{ w }}</li>
            </ul>
          </NAlert>
          <template #footer>
            <div class="flex justify-end">
              <NButton @click="validateModal.show = false">{{ t('admin.common.close') }}</NButton>
            </div>
          </template>
        </NModal>
      </div>
    </template>
  </TDetailLayout>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, h, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NAlert,
  NButton,
  NCard,
  NCheckbox,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NSelect,
  NSpin,
  NTab,
  NTabs,
  NTag,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailLayout from '../../../components/detail/TDetailLayout.vue'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { translatePageKey, interpolate } from '../../_shared/translate'
import {
  workflowNodeTypes,
  getNodeTypeMeta,
  NODE_TYPE_KEY,
  POS_X_KEY,
  POS_Y_KEY,
  type WorkflowNodeKind,
} from './workflow-node-types'
import type {
  AgentDto,
  WorkflowDefinitionDto,
  WorkflowStepDto,
  UpdateWorkflowDefinitionDto,
  WorkflowValidationResultDto,
} from '@tnzi/core/services/ai'
// 0.2.72+ (B4): enum value routed through ai-bridge so the page stays
// clean under the `no-restricted-imports` guard.
import { WorkflowExecutionMode } from '../../../services/bridges/ai-bridge'
// Re-exported from @tnzi/ui-ai → @vue-flow/core. Pulling them in here is fine
// because this route component is itself lazy-loaded (route-level chunk), so
// chat pages don't pay the vue-flow cost just by sharing the @tnzi/ui-ai
// barrel.
import { Handle, Position, type NodeProps } from '@tnzi/ui-ai'

// `@vue-flow/core` lives inside `@tnzi/ui-ai`'s dependency tree but isn't a
// direct ui-admin dep — type the canvas inputs structurally so we can keep
// the canvas import lazy without pulling vue-flow's type declarations into
// the ui-admin typecheck pass.
interface FlowNode {
  id: string
  type?: string
  // VueFlow accepts string OR VNode for `label`; we use VNode to inline an icon
  // beside the step id so canvas tiles read at a glance.
  label?: string | unknown
  position: { x: number; y: number }
  style?: Record<string, string>
  class?: string
  data?: Record<string, unknown>
}
interface FlowEdge {
  id: string
  source: string
  target: string
  type?: string
  label?: string
}

interface FlowConnection {
  source: string
  target: string
  sourceHandle?: string | null
  targetHandle?: string | null
}

interface FlowNodeChange {
  id?: string
  type?: string
  position?: { x: number; y: number }
}

interface FlowEdgeChange {
  id?: string
  type?: string
}

// Lazy-load the heavy vue-flow canvas wrapper from @tnzi/ui-ai. The fallback
// keeps the toolbar usable even while the chunk streams in.
const LoadingStub = {
  name: 'LoadingStub',
  render: () => h('div', { class: 't-wf-editor__placeholder' }, 'Loading canvas…'),
}

const WorkflowCanvas = defineAsyncComponent({
  loader: () => import('@tnzi/ui-ai').then((m) => m.WorkflowCanvas),
  loadingComponent: LoadingStub,
  delay: 200,
  timeout: 30000,
})

const route = useRoute()
const router = useRouter()
const bridge = createAiBridge({ client: useAdminClient() })
const message = useMessage()

const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.workflows', key), params)

// --- workflowId from route --------------------------------------------------
const workflowId = computed<string | undefined>(() => {
  const raw = route.params?.id
  if (Array.isArray(raw)) return raw[0]
  return typeof raw === 'string' && raw.length > 0 ? raw : undefined
})

// --- View mode --------------------------------------------------------------
const viewMode = ref<'visual' | 'json'>('visual')

// --- Load workflow ----------------------------------------------------------
const workflow = ref<WorkflowDefinitionDto | null>(null)
const loading = ref(false)
const loadError = ref('')
const saving = ref(false)
const dirty = ref(false)

interface DraftMetadata {
  name: string
  description: string
  executionMode: WorkflowDefinitionDto['executionMode']
  isEnabled: boolean
  steps: WorkflowStepDto[]
}

const draft = reactive<DraftMetadata>({
  name: '',
  description: '',
  executionMode: WorkflowExecutionMode.Sequential,
  isEnabled: true,
  steps: [],
})

const stepsJson = ref('[]')
const stepsJsonDirty = ref(false)
const stepsJsonError = ref('')

// --- Agents (for AgentId select) -------------------------------------------
const agents = ref<AgentDto[]>([])
const agentsLoading = ref(false)
const agentOptions = computed(() =>
  agents.value.map((a) => ({ label: a.name, value: a.id })),
)

async function loadAgents(): Promise<void> {
  agentsLoading.value = true
  try {
    const res = await bridge.agents.fetch({
      pageIndex: 1,
      pageSize: 200,
      searchText: '',
      filters: {},
    })
    agents.value = res.items
  } catch {
    // best-effort: dropdown stays empty
  } finally {
    agentsLoading.value = false
  }
}

const executionModeOptions = computed(() => [
  { label: t('executionMode.sequential'), value: WorkflowExecutionMode.Sequential },
  { label: t('executionMode.parallel'), value: WorkflowExecutionMode.Parallel },
  { label: t('executionMode.dag'), value: WorkflowExecutionMode.Dag },
])

const executionModeLabel = computed(() => {
  const found = executionModeOptions.value.find((o) => o.value === draft.executionMode)
  return found?.label ?? String(draft.executionMode)
})

async function loadWorkflow(id: string): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const wf = await bridge.workflows.getById(id)
    workflow.value = wf
    draft.name = wf.name
    draft.description = wf.description ?? ''
    draft.executionMode = wf.executionMode
    draft.isEnabled = wf.isEnabled
    draft.steps = ensureStepShape(wf.steps ?? [])
    stepsJson.value = JSON.stringify(draft.steps, null, 2)
    stepsJsonDirty.value = false
    stepsJsonError.value = ''
    dirty.value = false
    selectedStepId.value = null
  } catch (err) {
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

watch(
  workflowId,
  (id) => {
    if (id) void loadWorkflow(id)
  },
  { immediate: true },
)

// --- Steps JSON edit --------------------------------------------------------
function onStepsJsonChange(v: string): void {
  stepsJson.value = v
  stepsJsonDirty.value = true
  try {
    const parsed = JSON.parse(v) as WorkflowStepDto[]
    if (!Array.isArray(parsed)) {
      stepsJsonError.value = t('editor.stepsMustBeArray')
      return
    }
    stepsJsonError.value = ''
    draft.steps = ensureStepShape(parsed)
    dirty.value = true
  } catch (err) {
    stepsJsonError.value = err instanceof Error ? err.message : String(err)
  }
}

function formatStepsJson(): void {
  try {
    const parsed = JSON.parse(stepsJson.value)
    stepsJson.value = JSON.stringify(parsed, null, 2)
    stepsJsonError.value = ''
  } catch (err) {
    stepsJsonError.value = err instanceof Error ? err.message : String(err)
  }
}

// --- Selection state --------------------------------------------------------
const selectedStepId = ref<string | null>(null)
const selectedStep = computed<WorkflowStepDto | null>(() =>
  (draft.steps ?? []).find((s) => s.stepId === selectedStepId.value) ?? null,
)
const selectedKind = computed<WorkflowNodeKind>(() => getStepKind(selectedStep.value))
const selectedFields = computed(() => getNodeTypeMeta(selectedKind.value).fields)

const dependsOnOptions = computed(() =>
  (draft.steps ?? [])
    .filter((s) => s.stepId && s.stepId !== selectedStepId.value)
    .map((s) => ({ label: s.stepId!, value: s.stepId! })),
)

const nodeTypeMetas = workflowNodeTypes
const nodeTypeSelectOptions = computed(() =>
  workflowNodeTypes.map((m) => ({
    label: t(`nodeTypes.${m.labelKey}`),
    value: m.kind,
  })),
)

function selectStep(stepId: string | null | undefined): void {
  if (!stepId) return
  selectedStepId.value = stepId
}

/**
 * Rename the selected step's stepId. `selectedStep` is a computed match by
 * stepId, so blindly binding `v-model:value="selectedStep.stepId"` would orphan
 * the selection after a single keystroke (the computed find can't match the
 * new id, returns null, and the inspector swaps to the metadata panel mid-edit).
 *
 * Sync `selectedStepId` and rewrite any sibling `dependsOn` references that
 * pointed to the old id so the DAG stays internally consistent. Uniqueness is
 * not enforced here — the backend `validate` endpoint flags duplicates on save.
 */
function onStepIdInput(newId: string): void {
  const step = selectedStep.value
  if (!step) return
  const oldId = step.stepId
  step.stepId = newId
  if (oldId && oldId !== newId) {
    for (const s of draft.steps ?? []) {
      if (s.dependsOn?.includes(oldId)) {
        s.dependsOn = s.dependsOn.map((d) => (d === oldId ? newId : d))
      }
    }
    selectedStepId.value = newId
  }
  markDirty()
}

function markDirty(): void {
  if (!workflow.value) return
  dirty.value = true
  // Keep the JSON view in sync so switching tabs doesn't lose changes.
  stepsJson.value = JSON.stringify(draft.steps, null, 2)
}

function ensureStepShape(steps: WorkflowStepDto[]): WorkflowStepDto[] {
  // Defensive normalization on load — older workflow definitions may not have
  // a stepId or a configuration bag. Without a stepId the canvas can't render
  // and `dependsOn` references won't resolve, so we synthesize one.
  return steps.map((step, idx) => {
    const next: WorkflowStepDto = {
      ...step,
      stepId: step.stepId && step.stepId.trim() ? step.stepId : `step-${idx + 1}`,
      dependsOn: step.dependsOn ? [...step.dependsOn] : null,
      configuration: step.configuration ? { ...step.configuration } : {},
    }
    return next
  })
}

function getStepKind(step: WorkflowStepDto | null | undefined): WorkflowNodeKind {
  if (!step) return 'agent'
  const raw = step.configuration?.[NODE_TYPE_KEY]
  return getNodeTypeMeta(raw ?? 'agent').kind
}

function changeKind(kind: string): void {
  if (!selectedStep.value) return
  const cfg = selectedStep.value.configuration ?? {}
  selectedStep.value.configuration = { ...cfg, [NODE_TYPE_KEY]: kind }
  markDirty()
}

function nextStepId(base: string): string {
  let n = (draft.steps?.length ?? 0) + 1
  let candidate = `${base}-${n}`
  const seen = new Set((draft.steps ?? []).map((s) => s.stepId))
  while (seen.has(candidate)) {
    n += 1
    candidate = `${base}-${n}`
  }
  return candidate
}

function addStepOfKind(kind: WorkflowNodeKind): void {
  const id = nextStepId(kind)
  const meta = getNodeTypeMeta(kind)
  const last = (draft.steps ?? [])[draft.steps.length - 1]
  const newStep: WorkflowStepDto = {
    stepId: id,
    order: (draft.steps?.length ?? 0) + 1,
    maxRetries: meta.fields.retry ? 0 : 0,
    retryDelaySeconds: 2,
    requiresApproval: meta.fields.requiresApproval && kind === 'approval',
    dependsOn: last?.stepId ? [last.stepId] : null,
    configuration: {
      [NODE_TYPE_KEY]: kind,
      [POS_X_KEY]: String(120 + (draft.steps?.length ?? 0) * 220),
      [POS_Y_KEY]: String(120),
    },
  }
  draft.steps = [...(draft.steps ?? []), newStep]
  selectedStepId.value = id
  markDirty()
}

function duplicateStep(step: WorkflowStepDto): void {
  if (!step.stepId) return
  const id = nextStepId(step.stepId)
  const copy: WorkflowStepDto = {
    ...step,
    stepId: id,
    order: (draft.steps?.length ?? 0) + 1,
    dependsOn: step.dependsOn ? [...step.dependsOn] : null,
    configuration: {
      ...(step.configuration ?? {}),
      [POS_X_KEY]: String(
        Number.parseInt(step.configuration?.[POS_X_KEY] ?? '120', 10) + 40,
      ),
      [POS_Y_KEY]: String(
        Number.parseInt(step.configuration?.[POS_Y_KEY] ?? '120', 10) + 40,
      ),
    },
  }
  draft.steps = [...(draft.steps ?? []), copy]
  selectedStepId.value = id
  markDirty()
}

function deleteStep(stepId: string | undefined | null): void {
  if (!stepId) return
  draft.steps = (draft.steps ?? [])
    .filter((s) => s.stepId !== stepId)
    .map((s) => ({
      ...s,
      // Remove deleted node from any remaining DependsOn lists.
      dependsOn: s.dependsOn?.filter((d) => d !== stepId) ?? null,
    }))
  if (selectedStepId.value === stepId) selectedStepId.value = null
  markDirty()
}

// --- Canvas nodes/edges derived from draft.steps ----------------------------
//
// Stash the resolved type metadata (kind/icon/color) on `data` so the
// `#node-default` slot template can render the icon next to the label and
// the surrounding card border matches the left-side palette. `node.style`
// drives the outer wrapper border because the slot replaces inner content
// but doesn't own the outer vue-flow node frame.
const vfNodes = computed<FlowNode[]>(() =>
  (draft.steps ?? []).map((step, idx) => {
    const id = step.stepId ?? `step-${idx}`
    const meta = getNodeTypeMeta(getStepKind(step))
    const cfg = step.configuration ?? {}
    const x = Number.parseInt(cfg[POS_X_KEY] ?? '', 10)
    const y = Number.parseInt(cfg[POS_Y_KEY] ?? '', 10)
    return {
      id,
      type: 'default',
      label: id,
      position: {
        x: Number.isFinite(x) ? x : 120 + idx * 220,
        y: Number.isFinite(y) ? y : 120 + (idx % 3) * 40,
      },
      style: {
        borderColor: meta.color,
        borderWidth: '2px',
        background: `color-mix(in srgb, ${meta.color} 8%, var(--tnzi-container-bg))`,
        // Cap the node width so long stepIds get truncated with ellipsis
        // instead of pushing the border out. Width is fixed (not max-width)
        // so all canvas nodes share a tidy visual rhythm regardless of label.
        width: '200px',
        padding: '8px 12px',
      },
      data: {
        label: id,
        kind: meta.kind,
        icon: meta.icon,
        color: meta.color,
        agentId: step.agentId,
        requiresApproval: step.requiresApproval,
      },
    } as FlowNode
  }),
)

const vfEdges = computed<FlowEdge[]>(() => {
  const edges: FlowEdge[] = []
  for (const step of draft.steps ?? []) {
    const tgt = step.stepId
    if (!tgt) continue
    for (const dep of step.dependsOn ?? []) {
      edges.push({
        id: `${dep}__${tgt}`,
        source: dep,
        target: tgt,
        type: 'default',
      })
    }
  }
  return edges
})

// --- Canvas event handlers --------------------------------------------------
function onCanvasNodeClick(event: unknown): void {
  // Vue Flow's node-click emits `{ node: { id } }` — we don't depend on the
  // full type definition (kept structural here for tree-shaking), so we narrow
  // defensively.
  const node = (event as { node?: { id?: string } } | undefined)?.node
  if (node?.id) selectedStepId.value = node.id
}

function onCanvasNodesChange(changes: unknown[]): void {
  let touched = false
  for (const raw of changes) {
    const c = raw as FlowNodeChange
    if (c.type === 'position' && c.id && c.position) {
      const step = (draft.steps ?? []).find((s) => s.stepId === c.id)
      if (step) {
        step.configuration = {
          ...(step.configuration ?? {}),
          [POS_X_KEY]: String(Math.round(c.position.x)),
          [POS_Y_KEY]: String(Math.round(c.position.y)),
        }
        touched = true
      }
    } else if (c.type === 'remove' && c.id) {
      deleteStep(c.id)
      touched = true
    }
  }
  if (touched) markDirty()
}

function onCanvasEdgesChange(changes: unknown[]): void {
  let touched = false
  for (const raw of changes) {
    const c = raw as FlowEdgeChange
    if (c.type === 'remove' && c.id) {
      const [source, target] = c.id.split('__')
      if (!target) continue
      const step = (draft.steps ?? []).find((s) => s.stepId === target)
      if (step) {
        step.dependsOn = step.dependsOn?.filter((d) => d !== source) ?? null
        touched = true
      }
    }
  }
  if (touched) markDirty()
}

function onCanvasConnect(connection: unknown): void {
  const c = connection as FlowConnection | undefined
  if (!c?.source || !c?.target) return
  if (c.source === c.target) {
    message.warning(t('editor.selfDependencyBlocked'))
    return
  }
  const step = (draft.steps ?? []).find((s) => s.stepId === c.target)
  if (!step) return
  const existing = step.dependsOn ?? []
  if (existing.includes(c.source)) return
  step.dependsOn = [...existing, c.source]
  markDirty()
}

// --- Save -------------------------------------------------------------------
async function onSave(): Promise<void> {
  if (!workflow.value) return
  if (stepsJsonError.value && viewMode.value === 'json') {
    message.error(stepsJsonError.value)
    return
  }
  saving.value = true
  try {
    const payload: UpdateWorkflowDefinitionDto = {
      name: draft.name,
      description: draft.description || null,
      executionMode: draft.executionMode,
      isEnabled: draft.isEnabled,
      steps: draft.steps,
    }
    const updated = await bridge.workflows.update(workflow.value.id, payload)
    workflow.value = updated
    draft.steps = ensureStepShape(updated.steps ?? draft.steps)
    stepsJson.value = JSON.stringify(draft.steps, null, 2)
    dirty.value = false
    stepsJsonDirty.value = false
    message.success(t('editor.saved'))
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('editor.saveError'))
  } finally {
    saving.value = false
  }
}

// --- Toggle publish ---------------------------------------------------------
async function togglePublish(): Promise<void> {
  if (!workflow.value) return
  try {
    const updated = workflow.value.isEnabled
      ? await bridge.workflows.unpublish(workflow.value.id)
      : await bridge.workflows.publish(workflow.value.id)
    workflow.value = updated
    draft.isEnabled = updated.isEnabled
    message.success(updated.isEnabled ? t('actions.enableSuccess') : t('actions.disableSuccess'))
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('actions.toggleError'))
  }
}

// --- Validate ---------------------------------------------------------------
const validateModal = reactive<{ show: boolean; result: WorkflowValidationResultDto | null }>({
  show: false,
  result: null,
})

async function onValidate(): Promise<void> {
  if (!workflow.value) return
  validateModal.show = true
  validateModal.result = null
  try {
    validateModal.result = await bridge.workflows.validate(workflow.value.id)
  } catch (err) {
    validateModal.result = {
      isValid: false,
      errors: [err instanceof Error ? err.message : String(err)],
      warnings: [],
    }
  }
}

// --- Run --------------------------------------------------------------------
const runModal = reactive<{ show: boolean; input: string; running: boolean }>({
  show: false,
  input: '',
  running: false,
})

function openRun(): void {
  runModal.input = ''
  runModal.show = true
}

async function confirmRun(): Promise<void> {
  if (!workflow.value) return
  runModal.running = true
  try {
    const result = await bridge.workflows.run(workflow.value.id, runModal.input.trim())
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

// Load agents on mount.
void loadAgents()
</script>

<style scoped>
/* Inner body wrapper — fills TDetailLayout's scrollable body region */
.t-wf-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
}
/* Header title slot: workflow name + status badge inline */
.t-wf-editor__header-title {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.t-wf-editor__header-name {
  font-size: 18px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
/* View-mode tab bar (visual / JSON) rendered at the top of the body */
.t-wf-editor__view-bar {
  display: flex;
  align-items: center;
}
.t-wf-editor__view-tabs {
  width: 200px;
}
.t-wf-editor__loading,
.t-wf-editor__empty,
.t-wf-editor__placeholder {
  padding: 32px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
}
.t-wf-editor__body {
  display: grid;
  grid-template-columns: 260px 1fr 340px;
  gap: 12px;
  min-height: 600px;
  flex: 1;
}
@media (max-width: 1280px) {
  .t-wf-editor__body {
    grid-template-columns: 240px 1fr 320px;
  }
}
@media (max-width: 1024px) {
  .t-wf-editor__body {
    grid-template-columns: 1fr;
  }
}
.t-wf-editor__sidebar {
  min-width: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.t-wf-editor__canvas {
  min-width: 0;
}
.t-wf-editor__inspector {
  min-width: 0;
}
.t-wf-editor__palette {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
}
.t-wf-editor__palette-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  background: transparent;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  font-size: 12px;
  color: var(--tnzi-base-text);
  transition: background-color 0.15s, transform 0.05s;
}
.t-wf-editor__palette-btn:hover {
  background: var(--tnzi-layout-bg);
}
.t-wf-editor__palette-btn:active {
  transform: scale(0.97);
}
.t-wf-editor__node-list-card :deep(.n-card__content) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  padding-top: 8px;
}
.t-wf-editor__node-list {
  list-style: none;
  margin: 0;
  padding: 0;
  overflow: auto;
  flex: 1;
  min-height: 0;
}
.t-wf-editor__node-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  font-size: 13px;
  transition: background-color 0.15s;
}
.t-wf-editor__node-row:hover {
  background: var(--tnzi-layout-bg);
}
.t-wf-editor__node-row.is-active {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
}
.t-wf-editor__node-row-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: ui-monospace, monospace;
  font-size: 12.5px;
}
.t-wf-editor__node-list-empty {
  padding: 16px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
  font-size: 12.5px;
}
.t-wf-editor__stats-pill {
  display: inline-block;
  min-width: 22px;
  padding: 0 6px;
  text-align: center;
  font-size: 11px;
  border-radius: 10px;
  background: var(--tnzi-layout-bg);
  color: var(--tnzi-base-text-muted);
}
.t-wf-editor__canvas-card {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.t-wf-editor__canvas-card :deep(.n-card__content) {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0;
}
.t-wf-editor__canvas-card :deep(.n-card__content) > div {
  flex: 1;
  min-height: 540px;
}
.t-wf-editor__canvas-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  width: 100%;
  font-weight: 600;
}
.t-wf-editor__canvas-hint {
  font-size: 12px;
  font-weight: 400;
  color: var(--tnzi-base-text-muted);
}
/* Inside the `#node-default` custom node — icon + label aligned + tight gap.
   `min-width: 0` on the flex child below lets ellipsis trigger inside the
   200px node frame; without it the label would still overflow.
   Tooltip on hover (`title` attr on container) surfaces the full id. */
.t-wf-flow-node-content {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-family: ui-monospace, monospace;
  width: 100%;
  min-width: 0;
}
.t-wf-flow-node-icon {
  flex-shrink: 0;
}
.t-wf-flow-node-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-wf-editor__inspector-card {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.t-wf-editor__inspector-card :deep(.n-card__content) {
  flex: 1;
  overflow: auto;
}
/* `:show-feedback="false"` strips the 24px feedback slot below every form
   item, which would otherwise space them apart. Restore explicit spacing so
   the next item's label doesn't sit flush against the previous input. */
.t-wf-editor__inspector-card :deep(.n-form-item) {
  margin-bottom: 12px;
}
.t-wf-editor__inspector-card :deep(.n-form-item:last-child) {
  margin-bottom: 0;
}
.t-wf-editor__inspector-card :deep(.n-form-item .n-form-item-label) {
  padding-bottom: 4px;
}
.t-wf-editor__inspector-header {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
}
.t-wf-editor__inspector-footer {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}
.t-wf-editor__row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-bottom: 12px;
}
.t-wf-editor__row :deep(.n-form-item) {
  margin-bottom: 0;
}
.t-wf-editor__stats {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 13px;
}
.t-wf-editor__stats span {
  color: var(--tnzi-base-text-muted);
  margin-right: 6px;
}
.t-wf-editor__json-body {
  flex: 1;
  display: flex;
}
.t-wf-editor__steps-card {
  flex: 1;
  display: flex;
  flex-direction: column;
}
.t-wf-editor__steps-card :deep(.n-card__content) {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 12px;
}
.t-wf-editor__steps-input :deep(textarea) {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 12px;
  line-height: 1.55;
}
.t-wf-editor__validate-list {
  margin: 8px 0 0;
  padding-left: 16px;
  font-size: 13px;
}
</style>
