<template>
  <!--
    ToolPermissions — wraps /admin/permissions/* for Tnzi.AI tool guardrails.
    Three tabs:
      • Persisted Rules — full CRUD on ToolPermissionRuleEntity (DB-backed,
        survives restarts). Filtered by 4-scope tag (System/Project/User/
        Session) with priority sort.
      • Session Rules — read-only view of session-scope rules currently
        installed in IToolPermissionEvaluator (added by AddSessionRule).
      • Evaluate — debug/dry-run a context against the merged rule set;
        renders the matched rule pattern + scope + behavior + reason, plus a
        "decision chain" explainer (Priority → Scope weight → Behavior weight)
        so reviewers understand *why* a given decision was reached.

    Conflict resolution order (shown to the user): Priority desc → Scope weight
    desc (Session=4 > User=3 > Project=2 > System=1) → Behavior weight desc
    (Deny > Ask > Allow).
  -->
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
      <NButton size="small" type="primary" @click="openCreateModal">
        <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <template #default>
      <TKpiRow class="t-perm-page__kpis" cols="1 s:3">
        <TStatCard :label="t('kpi.persisted')" :value="persistedRules.length" />
        <TStatCard :label="t('kpi.session')" :value="sessionRules.length" />
        <TStatCard
          :label="t('kpi.hasRules')"
          :value="rulesSnapshot?.hasRules ? t('status.yes') : t('status.no')"
          :tone="rulesSnapshot?.hasRules ? 'success' : 'default'"
        />
      </TKpiRow>

      <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
        <NTabPane name="persisted" :tab="t('tabs.persisted')">
          <div class="t-table-tabs__pane">
            <div class="t-perm-page__hint">
              <TSvgIcon icon="mdi:sort-descending" :size="14" />
              <span>{{ t('decisionChain.sortHint') }}</span>
            </div>
            <TResponsiveTable
              :columns="persistedColumns"
              :data="sortedPersistedRules"
              :loading="loading"
              :pagination="{ pageSize: 20 }"
              :bordered="false"
              size="small"
              :flex-height="true"
            />
          </div>
        </NTabPane>

        <NTabPane name="session" :tab="t('tabs.session')">
          <div class="t-table-tabs__pane">
            <TResponsiveTable
              :columns="sessionColumns"
              :data="sessionRules"
              :loading="loading"
              :pagination="{ pageSize: 20 }"
              :bordered="false"
              size="small"
              :flex-height="true"
            />
          </div>
        </NTabPane>

        <NTabPane name="evaluate" :tab="t('tabs.evaluate')">
          <div class="t-perm-page__eval">
            <NForm :label-width="140" label-placement="left" class="t-perm-page__form">
              <NFormItem :label="t('form.toolName')">
                <NInput v-model:value="evalCtx.toolName" :placeholder="t('form.toolNamePlaceholder')" />
              </NFormItem>
              <NFormItem :label="t('form.toolGroup')">
                <NInput v-model:value="evalCtx.toolGroup" :placeholder="t('form.optional')" />
              </NFormItem>
              <NFormItem :label="t('form.serverName')">
                <NInput v-model:value="evalCtx.serverName" :placeholder="t('form.optional')" />
              </NFormItem>
              <NFormItem :label="t('form.shellCommand')">
                <NInput v-model:value="evalCtx.shellCommand" :placeholder="t('form.optional')" />
              </NFormItem>
              <NFormItem :label="t('form.isSubAgent')">
                <NSwitch v-model:value="evalCtx.isSubAgent" />
              </NFormItem>
              <NFormItem :label="t('form.isDestructive')">
                <NSwitch v-model:value="evalCtx.isDestructive" />
              </NFormItem>
              <NButton type="primary" :loading="evalLoading" :disabled="!evalCtx.toolName" @click="runEvaluate">
                {{ t('actions.evaluate') }}
              </NButton>
            </NForm>
            <NCard v-if="evalResult" :title="t('eval.result')" size="small" :bordered="false" class="t-perm-page__eval-result">
              <!-- Big, colour-coded decision badge — the headline verdict. -->
              <div class="t-perm-page__decision" :class="`t-perm-page__decision--${behaviorTone(evalResult.behavior)}`">
                <TSvgIcon :icon="behaviorIcon(evalResult.behavior)" :size="22" />
                <span class="t-perm-page__decision-text">{{ behaviorLabel(evalResult.behavior).toUpperCase() }}</span>
                <span class="t-perm-page__decision-tool">{{ evalResult.toolName }}</span>
              </div>

              <div class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('eval.scope') }}:</span>
                <NTag v-if="evalResult.scope != null" size="small" :bordered="false" type="info">
                  {{ scopeLabel(evalResult.scope) }} · {{ t('scope.weight', { n: scopeWeight(evalResult.scope) }) }}
                </NTag>
                <span v-else>—</span>
              </div>
              <div class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('eval.behavior') }}:</span>
                <NTag size="small" :bordered="false" :type="behaviorTone(evalResult.behavior)">
                  {{ behaviorLabel(evalResult.behavior) }} · {{ t('behavior.weight', { n: behaviorWeight(evalResult.behavior) }) }}
                </NTag>
              </div>
              <div class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('eval.matchedPattern') }}:</span>
                <code>{{ evalResult.matchedRulePattern ?? '—' }}</code>
              </div>
              <div v-if="evalResult.matchedToolGroup" class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('cols.toolGroup') }}:</span>
                <code>{{ evalResult.matchedToolGroup }}</code>
              </div>
              <div v-if="evalResult.matchedServerName" class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('cols.serverName') }}:</span>
                <code>{{ evalResult.matchedServerName }}</code>
              </div>
              <div v-if="evalResult.reason" class="t-perm-page__eval-row">
                <span class="t-perm-page__eval-label">{{ t('eval.reason') }}:</span>
                <span>{{ evalResult.reason }}</span>
              </div>

              <!-- Decision chain — explains the 3-stage conflict resolution so
                   reviewers can reason about why this verdict won. -->
              <NDivider class="t-perm-page__decision-divider" />
              <div class="t-perm-page__chain-title">{{ t('decisionChain.title') }}</div>
              <ol class="t-perm-page__chain">
                <li>{{ t('decisionChain.priority') }}</li>
                <li>{{ t('decisionChain.scope') }}</li>
                <li>{{ t('decisionChain.behavior') }}</li>
              </ol>
            </NCard>
          </div>
        </NTabPane>
      </NTabs>

      <NModal
        v-model:show="showCreateModal"
        :title="t('modal.create')"
        preset="card"
        class="w-640px"
        :mask-closable="false"
      >
        <NForm :label-width="140" label-placement="left">
          <NFormItem :label="t('form.toolPattern')" required>
            <NInput v-model:value="newRule.toolPattern" placeholder="e.g. shell:* or write_file" />
          </NFormItem>
          <NFormItem :label="t('form.behavior')" required>
            <NSelect
              v-model:value="newRule.behavior"
              :options="[
                { value: 0, label: t('behavior.allow') },
                { value: 1, label: t('behavior.ask') },
                { value: 2, label: t('behavior.deny') },
              ]"
            />
          </NFormItem>
          <NFormItem :label="t('form.scope')" required>
            <NSelect
              v-model:value="newRule.scope"
              :options="[
                { value: 0, label: t('scope.system') },
                { value: 1, label: t('scope.project') },
                { value: 2, label: t('scope.user') },
                { value: 3, label: t('scope.session') },
              ]"
            />
          </NFormItem>
          <NFormItem :label="t('form.priority')">
            <NInputNumber v-model:value="newRule.priority" :min="0" :max="1000" class="w-full" />
          </NFormItem>
          <NFormItem :label="t('form.toolGroup')">
            <NInput v-model:value="newRule.toolGroup" :placeholder="t('form.optional')" />
          </NFormItem>
          <NFormItem :label="t('form.commandPrefix')">
            <NInput v-model:value="newRule.commandPrefix" :placeholder="t('form.optional')" />
          </NFormItem>
          <NFormItem :label="t('form.serverName')">
            <NInput v-model:value="newRule.serverName" :placeholder="t('form.optional')" />
          </NFormItem>
          <NFormItem :label="t('form.pathPrefix')">
            <NInput v-model:value="newRule.pathPrefix" :placeholder="t('form.optional')" />
          </NFormItem>
          <NFormItem :label="t('form.reason')">
            <NInput v-model:value="newRule.reason" type="textarea" :rows="2" :placeholder="t('form.reasonPlaceholder')" />
          </NFormItem>
          <NFormItem :label="t('form.isDestructiveOnly')">
            <NSwitch v-model:value="newRule.isDestructiveOnly" />
          </NFormItem>
          <NFormItem :label="t('form.isSubAgentOnly')">
            <NSwitch v-model:value="newRule.isSubAgentOnly" />
          </NFormItem>
          <NFormItem :label="t('form.isEnabled')">
            <NSwitch v-model:value="newRule.isEnabled" />
          </NFormItem>
        </NForm>
        <template #footer>
          <NSpace justify="end">
            <NButton @click="showCreateModal = false">{{ t('actions.cancel') }}</NButton>
            <NButton type="primary" :loading="createLoading" :disabled="!newRule.toolPattern" @click="submitCreate">
              {{ t('actions.save') }}
            </NButton>
          </NSpace>
        </template>
      </NModal>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  NTabPane,
  NTabs,
  NTag,
  NTooltip,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TStatCard from '../../../components/data/TStatCard.vue'
import { useAdminClient } from '../../../plugin/client'
import {
  createPermissionBridge,
  type CreatePersistedPermissionRuleDto,
  type PermissionBehavior,
  type PermissionEvaluateRequestDto,
  type PermissionEvaluateResultDto,
  type PermissionRuleItemDto,
  type PermissionRulesDto,
  type PersistedPermissionRuleDto,
  type ToolPermissionScope,
} from '../../../services/bridges/permission-bridge'
import { interpolate, translatePageKey } from '../../_shared/translate'
import TContentPage from '../../../components/layout/TContentPage.vue'

const bridge = createPermissionBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.permissions', key), params)

const loading = ref(false)
const activeTab = ref<'persisted' | 'session' | 'evaluate'>('persisted')
const rulesSnapshot = ref<PermissionRulesDto | null>(null)
const persistedRules = ref<PersistedPermissionRuleDto[]>([])
const sessionRules = ref<PermissionRuleItemDto[]>([])

function behaviorTone(b: PermissionBehavior): 'success' | 'warning' | 'error' {
  switch (b) {
    case 0: return 'success'
    case 1: return 'warning'
    case 2: return 'error'
    default: return 'error'
  }
}
function behaviorLabel(b: PermissionBehavior): string {
  switch (b) {
    case 0: return t('behavior.allow')
    case 1: return t('behavior.ask')
    case 2: return t('behavior.deny')
    default: return String(b)
  }
}
function scopeLabel(s: ToolPermissionScope): string {
  switch (s) {
    case 0: return t('scope.system')
    case 1: return t('scope.project')
    case 2: return t('scope.user')
    case 3: return t('scope.session')
    default: return String(s)
  }
}
// Conflict-resolution weights (mirror the backend evaluator):
//   Scope:    Session=4 > User=3 > Project=2 > System=1
//   Behavior: Deny=2 (highest) > Ask=1 > Allow=0
function scopeWeight(s: ToolPermissionScope): number {
  switch (s) {
    case 3: return 4 // Session
    case 2: return 3 // User
    case 1: return 2 // Project
    case 0: return 1 // System
    default: return 0
  }
}
function behaviorWeight(b: PermissionBehavior): number {
  switch (b) {
    case 2: return 2 // Deny — wins ties
    case 1: return 1 // Ask
    case 0: return 0 // Allow
    default: return 0
  }
}
function behaviorIcon(b: PermissionBehavior): string {
  switch (b) {
    case 0: return 'mdi:check-circle'
    case 1: return 'mdi:help-circle'
    case 2: return 'mdi:close-circle'
    default: return 'mdi:help-circle'
  }
}

// Persisted rules sorted the way the evaluator resolves conflicts:
// Priority desc, then Scope weight desc. Gives reviewers a top-to-bottom
// "who wins" reading order without mutating the source array.
const sortedPersistedRules = computed(() =>
  [...persistedRules.value].sort(
    (a, b) => b.priority - a.priority || scopeWeight(b.scope) - scopeWeight(a.scope),
  ),
)
const persistedColumns: DataTableColumns<PersistedPermissionRuleDto> = [
  {
    title: () => t('cols.behavior'),
    key: 'behavior',
    width: 110,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: behaviorTone(row.behavior) }, () => behaviorLabel(row.behavior)),
  },
  {
    title: () => t('cols.priority'),
    key: 'priority',
    width: 90,
    align: 'right',
    sorter: (a, b) => a.priority - b.priority,
    defaultSortOrder: 'descend',
  },
  {
    title: () => t('cols.scope'),
    key: 'scope',
    width: 120,
    render: (row) =>
      h(
        NTooltip,
        { trigger: 'hover' },
        {
          trigger: () => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => scopeLabel(row.scope)),
          default: () => t('scope.weight', { n: scopeWeight(row.scope) }),
        },
      ),
  },
  {
    title: () => t('cols.toolPattern'),
    key: 'toolPattern',
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.toolPattern ?? '*'),
  },
  { title: () => t('cols.toolGroup'), key: 'toolGroup', width: 120, render: (r) => r.toolGroup ?? '—' },
  { title: () => t('cols.serverName'), key: 'serverName', width: 120, render: (r) => r.serverName ?? '—' },
  {
    title: () => t('cols.flags'),
    key: 'flags',
    width: 140,
    render: (row) =>
      h('div', { class: 'flex flex-wrap gap-4px' }, [
        row.isDestructiveOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'warning' }, () => 'destructive') : null,
        row.isSubAgentOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => 'subagent') : null,
        !row.isEnabled ? h(NTag, { size: 'tiny', bordered: false }, () => t('status.disabled')) : null,
      ]),
  },
  {
    title: () => t('cols.creationTime'),
    key: 'creationTime',
    width: 170,
    render: (row) => formatDate(row.creationTime),
  },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 100,
    align: 'right',
    render: (row) =>
      h(
        NPopconfirm,
        { onPositiveClick: () => deleteRule(row.id) },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'error', tertiary: true },
              {
                icon: () => h(TSvgIcon, { icon: 'mdi:delete-outline', size: 12 }),
                default: () => t('actions.delete'),
              },
            ),
          default: () => t('deleteConfirm'),
        },
      ),
  },
]

const sessionColumns: DataTableColumns<PermissionRuleItemDto> = [
  {
    title: () => t('cols.behavior'),
    key: 'behavior',
    width: 110,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: behaviorTone(row.behavior) }, () => behaviorLabel(row.behavior)),
  },
  {
    title: () => t('cols.priority'),
    key: 'priority',
    width: 90,
    align: 'right',
  },
  {
    title: () => t('cols.scope'),
    key: 'scope',
    width: 110,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => scopeLabel(row.scope)),
  },
  {
    title: () => t('cols.toolPattern'),
    key: 'toolPattern',
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.toolPattern),
  },
  { title: () => t('cols.toolGroup'), key: 'toolGroup', width: 120, render: (r) => r.toolGroup ?? '—' },
  { title: () => t('cols.reason'), key: 'reason', ellipsis: { tooltip: true }, render: (r) => r.reason ?? '—' },
]

// ─── Evaluate tab ──────────────────────────────────────────────────
const evalLoading = ref(false)
const evalResult = ref<PermissionEvaluateResultDto | null>(null)
const evalCtx = reactive<PermissionEvaluateRequestDto>({
  toolName: '',
  toolGroup: '',
  serverName: '',
  shellCommand: '',
  isSubAgent: false,
  isDestructive: false,
})

async function runEvaluate(): Promise<void> {
  if (!evalCtx.toolName) return
  evalLoading.value = true
  try {
    const req: PermissionEvaluateRequestDto = {
      toolName: evalCtx.toolName,
      toolGroup: evalCtx.toolGroup || null,
      serverName: evalCtx.serverName || null,
      shellCommand: evalCtx.shellCommand || null,
      isSubAgent: evalCtx.isSubAgent ?? false,
      isDestructive: evalCtx.isDestructive ?? false,
    }
    evalResult.value = await bridge.evaluate(req)
  } catch {
    evalResult.value = null
  } finally {
    evalLoading.value = false
  }
}

// ─── Create modal ──────────────────────────────────────────────────
const showCreateModal = ref(false)
const createLoading = ref(false)
const newRule = reactive<CreatePersistedPermissionRuleDto>({
  toolPattern: '',
  behavior: 0,
  scope: 0,
  priority: 100,
  isDestructiveOnly: false,
  isSubAgentOnly: false,
  isEnabled: true,
})

function openCreateModal(): void {
  newRule.toolPattern = ''
  newRule.toolGroup = null
  newRule.commandPrefix = null
  newRule.serverName = null
  newRule.pathPrefix = null
  newRule.reason = null
  newRule.behavior = 0
  newRule.scope = 0
  newRule.priority = 100
  newRule.isDestructiveOnly = false
  newRule.isSubAgentOnly = false
  newRule.isEnabled = true
  showCreateModal.value = true
}

async function submitCreate(): Promise<void> {
  if (!newRule.toolPattern) return
  createLoading.value = true
  try {
    await bridge.createPersistedRule({ ...newRule })
    showCreateModal.value = false
    await refresh()
  } catch { /* bridge swallows */ } finally {
    createLoading.value = false
  }
}

async function deleteRule(id: string): Promise<void> {
  try {
    await bridge.deletePersistedRule(id)
    await refresh()
  } catch { /* bridge swallows */ }
}

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [snapshot, persisted] = await Promise.all([
      bridge.getRules(),
      bridge.getPersistedRules(),
    ])
    rulesSnapshot.value = snapshot
    sessionRules.value = snapshot?.sessionRules ?? []
    persistedRules.value = persisted
  } catch {
    rulesSnapshot.value = null
    sessionRules.value = []
    persistedRules.value = []
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-perm-page__eval {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 24px;
  /* Form content in the Evaluate tab — let the pane scroll if the
     result card grows past the visible tab area. */
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  align-content: start;
}
@media (max-width: 900px) {
  .t-perm-page__eval { grid-template-columns: 1fr; }
}
.t-perm-page__form {
  max-width: 480px;
}
.t-perm-page__eval-result {
  align-self: start;
}
.t-perm-page__eval-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.t-perm-page__eval-label {
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
  min-width: 120px;
}

/* Sort hint above the persisted-rules table. */
.t-perm-page__hint {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 8px;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
}

/* Big colour-coded decision banner in the Evaluate result card. */
.t-perm-page__decision {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  margin-bottom: 12px;
  border-radius: 8px;
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.04em;
}
.t-perm-page__decision-tool {
  margin-left: auto;
  font-size: 13px;
  font-weight: 500;
  font-family: var(--tnzi-font-mono, monospace);
  opacity: 0.85;
}
.t-perm-page__decision--success {
  color: var(--tnzi-success, #18a058);
  background: rgba(24, 160, 88, 0.12);
}
.t-perm-page__decision--warning {
  color: var(--tnzi-warning, #f0a020);
  background: rgba(240, 160, 32, 0.12);
}
.t-perm-page__decision--error {
  color: var(--tnzi-error, #d03050);
  background: rgba(208, 48, 80, 0.12);
}

.t-perm-page__decision-divider {
  margin: 12px 0 8px;
}
.t-perm-page__chain-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 4px;
}
.t-perm-page__chain {
  margin: 0;
  padding-left: 18px;
  font-size: 12px;
  line-height: 1.7;
  color: var(--tnzi-base-text-2, #666);
}
</style>
