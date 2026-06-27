/**
 * Config for the ToolPermissions "Persisted Rules" tab (TCrudPage).
 *
 * Exports the shared behavior/scope helpers (reused by the page's Evaluate
 * result card + Session columns), the persisted-rules `ColumnDef[]` factory,
 * and the 12-field create/edit form schema. The page used to hand-roll a
 * 12-field NModal+NForm and a bespoke delete column; this config feeds the
 * standard TCrudPage + TFormSchemaRenderer + declarative row actions instead.
 *
 * The persisted rules are full CRUD: the permission admin controller exposes
 * GET/POST/PUT/DELETE persisted-rules endpoints, so the page wires create,
 * edit and delete.
 */
import { h } from 'vue'
import { NTag, NTooltip } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import type {
  PersistedPermissionRuleDto,
  PermissionBehavior,
  ToolPermissionScope,
} from '../../../services/bridges/permission-bridge'

type Translate = (key: string, params?: Record<string, unknown>) => string

// ── Conflict-resolution helpers (mirror the backend evaluator) ──────────────
//   Scope:    Session=4 > User=3 > Project=2 > System=1
//   Behavior: Deny=2 (wins ties) > Ask=1 > Allow=0
export function behaviorTone(b: PermissionBehavior): 'success' | 'warning' | 'error' {
  switch (b) {
    case 0: return 'success'
    case 1: return 'warning'
    case 2: return 'error'
    default: return 'error'
  }
}
export function behaviorIcon(b: PermissionBehavior): string {
  switch (b) {
    case 0: return 'mdi:check-circle'
    case 1: return 'mdi:help-circle'
    case 2: return 'mdi:close-circle'
    default: return 'mdi:help-circle'
  }
}
export function scopeWeight(s: ToolPermissionScope): number {
  switch (s) {
    case 3: return 4 // Session
    case 2: return 3 // User
    case 1: return 2 // Project
    case 0: return 1 // System
    default: return 0
  }
}
export function behaviorWeight(b: PermissionBehavior): number {
  switch (b) {
    case 2: return 2 // Deny
    case 1: return 1 // Ask
    case 0: return 0 // Allow
    default: return 0
  }
}
export function behaviorLabel(b: PermissionBehavior, t: Translate): string {
  switch (b) {
    case 0: return t('behavior.allow')
    case 1: return t('behavior.ask')
    case 2: return t('behavior.deny')
    default: return String(b)
  }
}
export function scopeLabel(s: ToolPermissionScope, t: Translate): string {
  switch (s) {
    case 0: return t('scope.system')
    case 1: return t('scope.project')
    case 2: return t('scope.user')
    case 3: return t('scope.session')
    default: return String(s)
  }
}

/** Persisted-rules columns (no action column — TCrudPage's declarative row
 *  actions own the delete affordance). Titles are i18n keys the page translates.
 *  Returns the loosely-typed `ColumnDef[]` that useCrudPage/TCrudPage accept
 *  (the DTO has required fields, so `ColumnDef<DTO>[]` isn't directly assignable
 *  to the default `ColumnDef<Record>[]`); render bodies stay DTO-typed. */
export function buildPersistedColumns(t: Translate): ColumnDef[] {
  const cols: ColumnDef<PersistedPermissionRuleDto>[] = [
    {
      key: 'behavior',
      title: 'cols.behavior',
      width: 110,
      render: (row) => h(NTag, { size: 'small', bordered: false, type: behaviorTone(row.behavior) }, () => behaviorLabel(row.behavior, t)),
    },
    { key: 'priority', title: 'cols.priority', width: 90, render: (row) => String(row.priority ?? '—') },
    {
      key: 'scope',
      title: 'cols.scope',
      width: 120,
      render: (row) =>
        h(
          NTooltip,
          { trigger: 'hover' },
          {
            trigger: () => h(NTag, { size: 'small', bordered: false, type: 'info' }, () => scopeLabel(row.scope, t)),
            default: () => t('scope.weight', { n: scopeWeight(row.scope) }),
          },
        ),
    },
    {
      key: 'toolPattern',
      title: 'cols.toolPattern',
      minWidth: 160,
      render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.toolPattern ?? '*'),
    },
    { key: 'toolGroup', title: 'cols.toolGroup', width: 120, render: (r) => r.toolGroup ?? '—' },
    { key: 'serverName', title: 'cols.serverName', width: 120, render: (r) => r.serverName ?? '—' },
    {
      key: 'flags',
      title: 'cols.flags',
      width: 150,
      render: (row) =>
        h('div', { class: 'flex flex-wrap gap-4px' }, [
          row.isDestructiveOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'warning' }, () => 'destructive') : null,
          row.isSubAgentOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => 'subagent') : null,
          !row.isEnabled ? h(NTag, { size: 'tiny', bordered: false }, () => t('status.disabled')) : null,
        ]),
    },
    { key: 'creationTime', title: 'cols.creationTime', width: 170, render: (row) => formatDateTime(row.creationTime) },
  ]
  return cols as unknown as ColumnDef[]
}

/** Create/edit form schema (12 fields). behavior/scope are required selects;
 *  the page injects sensible defaults (behavior=Allow, scope=System,
 *  priority=100, isEnabled=true) when the create modal opens (edit prefills
 *  from the row). */
export const persistedFormSchema: FormSchemaItem[] = [
  { key: 'toolPattern', labelKey: 'form.toolPattern', label: 'Tool Pattern', type: 'text', required: true, placeholder: 'e.g. shell:* or write_file' },
  {
    key: 'behavior',
    labelKey: 'form.behavior', label: 'Behavior',
    type: 'select',
    required: true,
    options: [
      { value: 0, labelKey: 'behavior.allow', label: 'Allow' },
      { value: 1, labelKey: 'behavior.ask', label: 'Ask' },
      { value: 2, labelKey: 'behavior.deny', label: 'Deny' },
    ],
  },
  {
    key: 'scope',
    labelKey: 'form.scope', label: 'Scope',
    type: 'select',
    required: true,
    options: [
      { value: 0, labelKey: 'scope.system', label: 'System' },
      { value: 1, labelKey: 'scope.project', label: 'Project' },
      { value: 2, labelKey: 'scope.user', label: 'User' },
      { value: 3, labelKey: 'scope.session', label: 'Session' },
    ],
  },
  { key: 'priority', labelKey: 'form.priority', label: 'Priority', type: 'number', min: 0, max: 1000 },
  { key: 'toolGroup', labelKey: 'form.toolGroup', label: 'Tool Group', type: 'text', placeholderKey: 'form.optional' },
  { key: 'commandPrefix', labelKey: 'form.commandPrefix', label: 'Command Prefix', type: 'text', placeholderKey: 'form.optional' },
  { key: 'serverName', labelKey: 'form.serverName', label: 'Server Name', type: 'text', placeholderKey: 'form.optional' },
  { key: 'pathPrefix', labelKey: 'form.pathPrefix', label: 'Path Prefix', type: 'text', placeholderKey: 'form.optional' },
  { key: 'reason', labelKey: 'form.reason', label: 'Reason', type: 'textarea', placeholderKey: 'form.reasonPlaceholder' },
  { key: 'isDestructiveOnly', labelKey: 'form.isDestructiveOnly', label: 'Destructive only', type: 'switch' },
  { key: 'isSubAgentOnly', labelKey: 'form.isSubAgentOnly', label: 'Sub-agent only', type: 'switch' },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]
