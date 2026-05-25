<template>
  <!--
    PermissionRules — wraps /admin/permissions/* for Tnzi.AI tool guardrails.
    Three tabs:
      • Persisted Rules — full CRUD on ToolPermissionRuleEntity (DB-backed,
        survives restarts). Filtered by 4-scope tag (System/Project/User/
        Session) with priority sort.
      • Session Rules — read-only view of session-scope rules currently
        installed in IToolPermissionEvaluator (added by AddSessionRule).
      • Evaluate — debug/dry-run a context against the merged rule set;
        renders the matched rule pattern + scope + behavior + reason.
  -->
  <div class="t-perm-page t-stack-page">
    <div class="t-perm-page__header">
      <div class="t-perm-page__title">
        <TSvgIcon icon="mdi:shield-key-outline" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-perm-page__toolbar">
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
        <NButton size="small" type="primary" @click="openCreateModal">
          <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
          {{ t('actions.create') }}
        </NButton>
      </div>
    </div>

    <div class="t-perm-page__kpis">
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.persisted')" :value="persistedRules.length" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.session')" :value="sessionRules.length" />
      </NCard>
      <NCard size="small" :bordered="false">
        <NStatistic :label="t('kpi.hasRules')">
          <template #default>
            <NTag :type="rulesSnapshot?.hasRules ? 'success' : 'default'" size="small" :bordered="false">
              {{ rulesSnapshot?.hasRules ? t('status.yes') : t('status.no') }}
            </NTag>
          </template>
        </NStatistic>
      </NCard>
    </div>

    <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
      <NTabPane name="persisted" :tab="t('tabs.persisted')">
        <div class="t-table-tabs__pane">
          <NDataTable
            :columns="persistedColumns"
            :data="persistedRules"
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
          <NDataTable
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
            <div class="t-perm-page__eval-row">
              <span class="t-perm-page__eval-label">{{ t('eval.decision') }}:</span>
              <NTag :type="behaviorTone(evalResult.behavior)" size="medium" :bordered="false">
                {{ behaviorLabel(evalResult.behavior) }}
              </NTag>
            </div>
            <div class="t-perm-page__eval-row">
              <span class="t-perm-page__eval-label">{{ t('eval.scope') }}:</span>
              <span>{{ evalResult.scope != null ? scopeLabel(evalResult.scope) : '—' }}</span>
            </div>
            <div class="t-perm-page__eval-row">
              <span class="t-perm-page__eval-label">{{ t('eval.matchedPattern') }}:</span>
              <code>{{ evalResult.matchedRulePattern ?? '—' }}</code>
            </div>
            <div v-if="evalResult.reason" class="t-perm-page__eval-row">
              <span class="t-perm-page__eval-label">{{ t('eval.reason') }}:</span>
              <span>{{ evalResult.reason }}</span>
            </div>
          </NCard>
        </div>
      </NTabPane>
    </NTabs>

    <NModal
      v-model:show="showCreateModal"
      :title="t('modal.create')"
      preset="card"
      style="width: 640px"
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
          <NInputNumber v-model:value="newRule.priority" :min="0" :max="1000" style="width: 100%" />
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
  </div>
</template>

<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NStatistic,
  NSwitch,
  NTabPane,
  NTabs,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
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
  }
}
function behaviorLabel(b: PermissionBehavior): string {
  switch (b) {
    case 0: return t('behavior.allow')
    case 1: return t('behavior.ask')
    case 2: return t('behavior.deny')
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
function formatDate(iso: string | null | undefined): string {
  if (!iso) return ''
  try { return new Date(iso).toLocaleString() } catch { return iso }
}

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
    width: 110,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => scopeLabel(row.scope)),
  },
  {
    title: () => t('cols.toolPattern'),
    key: 'toolPattern',
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.toolPattern ?? '*'),
  },
  { title: () => t('cols.toolGroup'), key: 'toolGroup', width: 120, render: (r) => r.toolGroup ?? '—' },
  { title: () => t('cols.serverName'), key: 'serverName', width: 120, render: (r) => r.serverName ?? '—' },
  {
    title: () => t('cols.flags'),
    key: 'flags',
    width: 140,
    render: (row) =>
      h('div', { style: 'display: flex; flex-wrap: wrap; gap: 4px;' }, [
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
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.toolPattern),
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
/* Layout shell from shared `.t-stack-page` + `.t-table-tabs` utilities. */
.t-perm-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-perm-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-perm-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-perm-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-perm-page__kpis {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
@media (max-width: 768px) {
  .t-perm-page__kpis { grid-template-columns: 1fr; }
}
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
</style>
