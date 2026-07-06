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
import type { StatusType } from '@tnzi/ui'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import type {
  PersistedPermissionRuleDto,
  PermissionBehavior,
  ToolPermissionScope,
} from '../../../services/bridges/permission-bridge'

type Translate = (key: string, params?: Record<string, unknown>) => string

// Absolute i18n namespace for this page — TStatusBadge's admin wrapper
// resolves `labelKey` from the locale root, so the mapping keys must be fully
// qualified (not the page-scoped short keys the page translator accepts).
const PERM_NS = 'admin.modules.ai.permissions'

// ── Enum normalisation ──────────────────────────────────────────────────────
// The backend serialises these enums as their PascalCase member NAME
// (JsonStringEnumConverter); a numeric ordinal is still accepted for backward
// compatibility. Every helper below normalises through these so a `"Deny"`
// string OR a legacy `2` both resolve to the same tone/weight/label.
type BehaviorName = 'Allow' | 'Ask' | 'Deny'
type ScopeName = 'System' | 'Project' | 'User' | 'Session'

/** Normalise a behavior value (member name OR ordinal) to its member name. */
export function behaviorName(b: PermissionBehavior | string | number): BehaviorName {
  const map: Record<string, BehaviorName> = {
    '0': 'Allow', '1': 'Ask', '2': 'Deny',
    Allow: 'Allow', Ask: 'Ask', Deny: 'Deny',
  }
  return map[String(b)] ?? 'Allow'
}

/** Normalise a scope value (member name OR ordinal) to its member name. */
export function scopeName(s: ToolPermissionScope | string | number): ScopeName {
  const map: Record<string, ScopeName> = {
    '0': 'System', '1': 'Project', '2': 'User', '3': 'Session',
    System: 'System', Project: 'Project', User: 'User', Session: 'Session',
  }
  return map[String(s)] ?? 'System'
}

/** behavior enum → unified status pill. Both the member-name (`"Allow"`) and the
 *  legacy ordinal (`0`) key each entry so TStatusBadge's `String(value)` lookup
 *  resolves regardless of wire form. Shared by the persisted-rules columns
 *  (config) and the session-rules columns (page). */
export const behaviorBadgeMapping: Record<string, { type: StatusType; labelKey: string }> = {
  0: { type: 'success', labelKey: `${PERM_NS}.behavior.allow` },
  1: { type: 'warning', labelKey: `${PERM_NS}.behavior.ask` },
  2: { type: 'error', labelKey: `${PERM_NS}.behavior.deny` },
  Allow: { type: 'success', labelKey: `${PERM_NS}.behavior.allow` },
  Ask: { type: 'warning', labelKey: `${PERM_NS}.behavior.ask` },
  Deny: { type: 'error', labelKey: `${PERM_NS}.behavior.deny` },
}

/** scope enum → unified status pill (member-name + legacy-ordinal keys).
 *  All scopes share the neutral `info` tone (matches the pre-migration look);
 *  the relative weight is surfaced via the tooltip in the persisted columns. */
export const scopeBadgeMapping: Record<string, { type: StatusType; labelKey: string }> = {
  0: { type: 'info', labelKey: `${PERM_NS}.scope.system` },
  1: { type: 'info', labelKey: `${PERM_NS}.scope.project` },
  2: { type: 'info', labelKey: `${PERM_NS}.scope.user` },
  3: { type: 'info', labelKey: `${PERM_NS}.scope.session` },
  System: { type: 'info', labelKey: `${PERM_NS}.scope.system` },
  Project: { type: 'info', labelKey: `${PERM_NS}.scope.project` },
  User: { type: 'info', labelKey: `${PERM_NS}.scope.user` },
  Session: { type: 'info', labelKey: `${PERM_NS}.scope.session` },
}

// ── Conflict-resolution helpers (mirror the backend evaluator) ──────────────
//   Scope:    Session=4 > User=3 > Project=2 > System=1
//   Behavior: Deny=2 (wins ties) > Ask=1 > Allow=0
export function behaviorTone(b: PermissionBehavior | string | number): 'success' | 'warning' | 'error' {
  switch (behaviorName(b)) {
    case 'Allow': return 'success'
    case 'Ask': return 'warning'
    case 'Deny': return 'error'
    default: return 'error'
  }
}
export function behaviorIcon(b: PermissionBehavior | string | number): string {
  switch (behaviorName(b)) {
    case 'Allow': return 'mdi:check-circle'
    case 'Ask': return 'mdi:help-circle'
    case 'Deny': return 'mdi:close-circle'
    default: return 'mdi:help-circle'
  }
}
export function scopeWeight(s: ToolPermissionScope | string | number): number {
  switch (scopeName(s)) {
    case 'Session': return 4
    case 'User': return 3
    case 'Project': return 2
    case 'System': return 1
    default: return 0
  }
}
export function behaviorWeight(b: PermissionBehavior | string | number): number {
  switch (behaviorName(b)) {
    case 'Deny': return 2
    case 'Ask': return 1
    case 'Allow': return 0
    default: return 0
  }
}
export function behaviorLabel(b: PermissionBehavior | string | number, t: Translate): string {
  switch (behaviorName(b)) {
    case 'Allow': return t('behavior.allow')
    case 'Ask': return t('behavior.ask')
    case 'Deny': return t('behavior.deny')
    default: return String(b)
  }
}
export function scopeLabel(s: ToolPermissionScope | string | number, t: Translate): string {
  switch (scopeName(s)) {
    case 'System': return t('scope.system')
    case 'Project': return t('scope.project')
    case 'User': return t('scope.user')
    case 'Session': return t('scope.session')
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
      render: (row) => h(TStatusBadge, { value: row.behavior, mapping: behaviorBadgeMapping }),
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
            trigger: () => h(TStatusBadge, { value: row.scope, mapping: scopeBadgeMapping }),
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
          // `destructive` / `subagent` are decorative feature-flag chips (not a
          // semantic status value), so they stay plain NTags. The enabled state
          // is a real status → unified status pill.
          row.isDestructiveOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'warning' }, () => 'destructive') : null,
          row.isSubAgentOnly ? h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => 'subagent') : null,
          !row.isEnabled ? h(TStatusBadge, { value: false, size: 'tiny', type: 'default', labelKey: `${PERM_NS}.status.disabled` }) : null,
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
    // Option values are the enum MEMBER NAMES so an edit prefill matches the
    // wire form (JsonStringEnumConverter). The backend accepts strings on write.
    options: [
      { value: 'Allow', labelKey: 'behavior.allow', label: 'Allow' },
      { value: 'Ask', labelKey: 'behavior.ask', label: 'Ask' },
      { value: 'Deny', labelKey: 'behavior.deny', label: 'Deny' },
    ],
  },
  {
    key: 'scope',
    labelKey: 'form.scope', label: 'Scope',
    type: 'select',
    required: true,
    options: [
      { value: 'System', labelKey: 'scope.system', label: 'System' },
      { value: 'Project', labelKey: 'scope.project', label: 'Project' },
      { value: 'User', labelKey: 'scope.user', label: 'User' },
      { value: 'Session', labelKey: 'scope.session', label: 'Session' },
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
