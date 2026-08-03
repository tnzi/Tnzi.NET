<template>
  <!--
    ToolPermissions - wraps /admin/permissions/* for Tnzi.AI tool guardrails.
    Three tabs:
      • Persisted Rules - full CRUD on ToolPermissionRuleEntity (DB-backed,
        survives restarts) via the standard TCrudPage (declarative row actions +
        schema-driven create/edit form). Backed by the permission admin
        controller's GET/POST/PUT/DELETE persisted-rules endpoints.
      • Session Rules - read-only view of session-scope rules currently
        installed in IToolPermissionEvaluator (added by AddSessionRule).
      • Evaluate - debug/dry-run a context against the merged rule set.

    Conflict resolution order (shown to the user): Priority desc → Scope weight
    desc (Session=4 > User=3 > Project=2 > System=1) → Behavior weight desc
    (Deny > Ask > Allow).
  -->
  <TTabsPage :title="t('title')" :translate="t" :sections="tabs" default-section="persisted">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refreshAll">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <!-- Cross-tab summary strip sits above the tab surface (visible on every
         tab), matching the pre-migration layout. -->
    <template #kpis>
      <TKpiRow class="t-perm-page__kpis" cols="1 s:3">
        <TKpiCard :label="t('kpi.persisted')" :value="persistedCount" />
        <TKpiCard :label="t('kpi.session')" :value="sessionRules.length" />
        <TKpiCard
          :label="t('kpi.hasRules')"
          :value="rulesSnapshot?.hasRules ? t('status.yes') : t('status.no')"
          :tone="rulesSnapshot?.hasRules ? 'success' : 'default'"
        />
      </TKpiRow>
    </template>

    <!-- ─── Tab 1: Persisted Rules ─────────────────────────────── -->
    <template #persisted>
      <div class="t-perm-page__hint">
        <TSvgIcon icon="mdi:sort-descending" :size="14" />
        <span>{{ t('decisionChain.sortHint') }}</span>
      </div>
      <!-- show-header=false: the title + refresh live on the outer TTabsPage
           header; the shell's own header card would otherwise duplicate them. -->
      <TCrudPage
        :state="crud"
        :all-columns="persistedColumns"
        :form-modal-width="640"
        :show-header="false"
        :translate="t"
        :row-actions="rowActions"
      >
        <template #form="{ formData, mode }">
          <TFormSchemaRenderer
            :schema="persistedFormSchema"
            :model="(formData ?? {}) as Record<string, unknown>"
            :readonly="mode === 'view'"
            :translate="t"
            :columns="2"
          />
        </template>
      </TCrudPage>
    </template>

    <!-- ─── Tab 2: Session Rules ───────────────────────────────── -->
    <template #session>
      <TResponsiveTable
        :columns="sessionColumns"
        :data="sessionRules"
        :loading="loading"
        :pagination="{ pageSize: 20 }"
        :bordered="false"
        size="small"
        :flex-height="true"
      />
    </template>

    <!-- ─── Tab 3: Evaluate ────────────────────────────────────── -->
    <template #evaluate>
      <div class="t-perm-page__eval">
        <!-- Context input card. Labels sit on top (no cramped 140px left
             column), text fields fill a responsive 2-col grid, the two boolean
             flags are inline switches, and the primary "Evaluate" CTA is a
             full-width button anchored at the bottom of the card. -->
        <NCard :title="t('eval.context')" size="small" :bordered="false" class="t-perm-page__eval-form t-tab-card">
          <NForm label-placement="top" :show-feedback="false">
            <div class="t-perm-page__form-grid">
              <NFormItem :label="t('form.toolName')" required>
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
            </div>
            <div class="t-perm-page__switches">
              <label class="t-perm-page__switch">
                <NSwitch v-model:value="evalCtx.isSubAgent" size="small" />
                <span>{{ t('form.isSubAgent') }}</span>
              </label>
              <label class="t-perm-page__switch">
                <NSwitch v-model:value="evalCtx.isDestructive" size="small" />
                <span>{{ t('form.isDestructive') }}</span>
              </label>
            </div>
            <NButton block type="primary" :loading="evalLoading" :disabled="!evalCtx.toolName" @click="runEvaluate">
              <template #icon><TSvgIcon icon="mdi:gavel" :size="16" /></template>
              {{ t('actions.evaluate') }}
            </NButton>
          </NForm>
        </NCard>

        <NCard v-if="evalResult" :title="t('eval.result')" size="small" :bordered="false" class="t-perm-page__eval-result t-tab-card">
          <!-- Big, colour-coded decision badge - the headline verdict. -->
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
            <span v-else>{{ EMPTY_DASH }}</span>
          </div>
          <div class="t-perm-page__eval-row">
            <span class="t-perm-page__eval-label">{{ t('eval.behavior') }}:</span>
            <NTag size="small" :bordered="false" :type="behaviorTone(evalResult.behavior)">
              {{ behaviorLabel(evalResult.behavior) }} · {{ t('behavior.weight', { n: behaviorWeight(evalResult.behavior) }) }}
            </NTag>
          </div>
          <div class="t-perm-page__eval-row">
            <span class="t-perm-page__eval-label">{{ t('eval.matchedPattern') }}:</span>
            <code>{{ evalResult.matchedRulePattern ?? EMPTY_DASH }}</code>
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

          <!-- Decision chain - explains the 3-stage conflict resolution so
               reviewers can reason about why this verdict won. -->
          <NDivider class="t-perm-page__decision-divider" />
          <div class="t-perm-page__chain-title">{{ t('decisionChain.title') }}</div>
          <ol class="t-perm-page__chain">
            <li>{{ t('decisionChain.priority') }}</li>
            <li>{{ t('decisionChain.scope') }}</li>
            <li>{{ t('decisionChain.behavior') }}</li>
          </ol>
        </NCard>

        <!-- Empty state before the first evaluation runs. -->
        <div v-else class="t-perm-page__eval-placeholder">
          <TSvgIcon icon="mdi:shield-search-outline" :size="40" />
          <span>{{ t('eval.placeholder') }}</span>
        </div>
      </div>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  NButton,
  NCard,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NSwitch,
  NTag,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { h } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TKpiCard from '../../../components/data/TKpiCard.vue'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { useCrudPage } from '../../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../../headless/row-actions'
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
import { makePageTranslator } from '../../_shared/translate'
import {
  behaviorTone,
  behaviorIcon,
  behaviorWeight,
  scopeWeight,
  behaviorLabel as behaviorLabelBase,
  scopeLabel as scopeLabelBase,
  behaviorBadgeMapping,
  scopeBadgeMapping,
  buildPersistedColumns,
  persistedFormSchema,
} from './tool-permissions-config'

const bridge = createPermissionBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.permissions')

// t-bound label helpers (the pure tone/weight/icon helpers come straight from
// the config; the labelled ones need the page translator).
const behaviorLabel = (b: PermissionBehavior): string => behaviorLabelBase(b, t)
const scopeLabel = (s: ToolPermissionScope): string => scopeLabelBase(s, t)

const loading = ref(false)
// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
const tabs: TabSection[] = [
  { name: 'persisted', label: t('tabs.persisted') },
  { name: 'session', label: t('tabs.session') },
  { name: 'evaluate', label: t('tabs.evaluate') },
]
const rulesSnapshot = ref<PermissionRulesDto | null>(null)
const sessionRules = ref<PermissionRuleItemDto[]>([])

// ── Persisted rules (TCrudPage) ────────────────────────────────────────────
// fetch returns the full list (no server paging) sorted the way the evaluator
// resolves conflicts: Priority desc, then Scope weight desc.
const persistedColumns = buildPersistedColumns(t)
// The schema-form `required` flag is visual only (TSchemaForm generates no
// validation rule and useCrudPage.submit never calls validate), so guard the
// tool pattern here - a blank pattern would match far too broadly. The thrown
// error is surfaced as a toast by runWithErrorHandling, which re-throws and so
// keeps the modal open for correction (mirrors the old save-button disable).
function ensureToolPattern(data: Partial<PersistedPermissionRuleDto>): void {
  if (!String(data.toolPattern ?? '').trim()) {
    throw new Error(t('form.toolPatternRequired'))
  }
}

const crud = useCrudPage<PersistedPermissionRuleDto>({
  pageId: 'ai.permissions',
  permission: 'ai.permissions',
  columns: persistedColumns,
  rowKey: (r) => r.id,
  fetchData: async () => {
    const items = [...(await bridge.getPersistedRules())].sort(
      (a, b) => b.priority - a.priority || scopeWeight(b.scope) - scopeWeight(a.scope),
    )
    // All rules come back in one shot (no server paging) - return a complete
    // single-page PagedList so the CrudPageResult contract is satisfied.
    return {
      items,
      totalCount: items.length,
      pageIndex: 1,
      pageSize: items.length || 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    }
  },
  createData: (data) => {
    ensureToolPattern(data)
    return bridge.createPersistedRule(data as CreatePersistedPermissionRuleDto) as Promise<PersistedPermissionRuleDto>
  },
  updateData: (id, data) => {
    ensureToolPattern(data)
    return bridge.updatePersistedRule(String(id), data as CreatePersistedPermissionRuleDto) as Promise<PersistedPermissionRuleDto>
  },
  deleteData: (ids) => Promise.all(ids.map((id) => bridge.deletePersistedRule(String(id)))).then(() => undefined),
})

const persistedCount = computed(() => crud.total.value)
const rowActions: RowAction<PersistedPermissionRuleDto>[] = [editAction(crud), deleteAction(crud)]

// Seed create defaults when the modal opens (behavior=Allow, scope=System,
// priority=100, isEnabled=true) - useCrudPage opens create with an empty {}.
watch(crud.formModal.visible, (open) => {
  if (!open || crud.formModal.mode.value !== 'create') return
  const m = crud.formModal.formData.value as Record<string, unknown> | null
  if (m && m.priority == null && m.behavior == null) {
    // Seed the enum defaults as member-name strings to match the select option
    // values + the wire form (the backend accepts strings on write).
    Object.assign(m, {
      behavior: 'Allow',
      scope: 'System',
      priority: 100,
      isDestructiveOnly: false,
      isSubAgentOnly: false,
      isEnabled: true,
    })
  }
})

const sessionColumns: DataTableColumns<PermissionRuleItemDto> = [
  {
    title: () => t('cols.behavior'),
    key: 'behavior',
    width: 110,
    render: (row) => h(TStatusBadge, { value: row.behavior, mapping: behaviorBadgeMapping }),
  },
  { title: () => t('cols.priority'), key: 'priority', width: 90, align: 'right' },
  {
    title: () => t('cols.scope'),
    key: 'scope',
    width: 110,
    render: (row) => h(TStatusBadge, { value: row.scope, size: 'tiny', mapping: scopeBadgeMapping }),
  },
  {
    title: () => t('cols.toolPattern'),
    key: 'toolPattern',
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.toolPattern),
  },
  { title: () => t('cols.toolGroup'), key: 'toolGroup', width: 120, render: (r) => r.toolGroup ?? EMPTY_DASH },
  { title: () => t('cols.reason'), key: 'reason', ellipsis: { tooltip: true }, render: (r) => r.reason ?? EMPTY_DASH },
]

// ── Evaluate tab ──────────────────────────────────────────────────
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

// ── Snapshot (session rules + hasRules KPI) ─────────────────────────
async function refreshSnapshot(): Promise<void> {
  try {
    const snapshot = await bridge.getRules()
    rulesSnapshot.value = snapshot
    sessionRules.value = snapshot?.sessionRules ?? []
  } catch {
    rulesSnapshot.value = null
    sessionRules.value = []
  }
}

async function refreshAll(): Promise<void> {
  loading.value = true
  try {
    await Promise.all([crud.refresh(), refreshSnapshot()])
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void refreshSnapshot()
})
</script>

<style scoped>
.t-perm-page__eval {
  display: grid;
  grid-template-columns: minmax(0, 400px) 1fr;
  gap: 12px;
  /* Form content in the Evaluate tab - let the pane scroll if the
     result card grows past the visible tab area. */
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  align-content: start;
}
@media (max-width: 900px) {
  .t-perm-page__eval { grid-template-columns: 1fr; }
}
.t-perm-page__eval-form {
  align-self: start;
}
.t-perm-page__form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 12px;
}
@media (max-width: 520px) {
  .t-perm-page__form-grid { grid-template-columns: 1fr; }
}
.t-perm-page__switches {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  margin: 2px 0 14px;
}
.t-perm-page__switch {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--tnzi-base-text-2, #555);
  cursor: pointer;
}
.t-perm-page__eval-result {
  align-self: start;
}
.t-perm-page__eval-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  min-height: 220px;
  padding: 24px;
  text-align: center;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
  border: 1px dashed var(--tnzi-border, #e5e7eb);
  border-radius: var(--tnzi-admin-radius-md, 8px);
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
