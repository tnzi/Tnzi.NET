import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Skills page config (sibling of Skills.vue) — production-grade card page.
 *
 * Shapes derive from @tnzi/core/services/ai:
 *   SkillSummaryDto  — paged list rows (id/slug/scope/name/description/whenToUse/
 *                      tags/priority/version/author/enabled/source/isReadOnly/filePath)
 *   SkillDetailDto   — adds `content` (the full SKILL.md body, lazy-loaded by slug)
 *   CreateSkillDto / UpdateSkillDto — write payloads
 *
 * The page renders as a TCardPage grid; the card + drawer own presentation, so
 * `skillColumns` is no longer wired into a table — it is kept only for the
 * column-setting localStorage contract that useCrudPage initialises. The card
 * surfaces icon + name + scope/enabled/source badges + tags + description, and
 * the detail drawer lazy-loads the full content via bridge.skills.getBySlug.
 *
 * SCOPE NOTE: backend SkillScope is the enum { System=0, Tenant=1, User=2 }, but
 * the JSON serialiser may emit either the numeric ordinal or the string name.
 * `scopeLabelKey` below normalises both forms to a stable i18n badge key.
 *
 * SOURCE NOTE: file-source skills have no DB row → backend returns Guid.Empty
 * ('00000000-…') for `id` AND sets `isReadOnly=true`. Skills.vue's rowKey falls
 * back to `slug:${slug}` for those rows, and edit/delete are disabled for them.
 */

/** Card-page column defs — kept for the useColumnSettings localStorage contract only. */
export const skillColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name', width: 180 },
  { key: 'slug', title: 'columns.slug', width: 160 },
  { key: 'scope', title: 'columns.scope', width: 100 },
  { key: 'description', title: 'columns.description', ellipsis: { tooltip: true } },
  { key: 'enabled', title: 'columns.enabled', width: 110 },
  { key: 'source', title: 'columns.source', width: 110 },
]

/** SkillScope { System=0, Tenant=1, User=2 } — create/edit select options. */
export const skillScopeOptions: Array<{ label: string; value: string }> = [
  { label: 'System', value: 'System' },
  { label: 'Tenant', value: 'Tenant' },
  { label: 'User', value: 'User' },
]

/**
 * Normalise a scope value (numeric ordinal OR string name) to a stable badge
 * i18n key under `badge.scope.*`. Defensive against both System.Text.Json
 * serialisation forms.
 */
export function scopeLabelKey(scope: unknown): string {
  const map: Record<string, string> = {
    '0': 'badge.scope.system',
    '1': 'badge.scope.tenant',
    '2': 'badge.scope.user',
    System: 'badge.scope.system',
    Tenant: 'badge.scope.tenant',
    User: 'badge.scope.user',
  }
  return map[String(scope)] ?? 'badge.scope.user'
}

/** Scope badge colour (naive NTag `type`). */
export function scopeBadgeType(scope: unknown): 'warning' | 'info' | 'success' {
  const key = scopeLabelKey(scope)
  if (key.endsWith('system')) return 'warning'
  if (key.endsWith('tenant')) return 'info'
  return 'success'
}

/** Create/edit form schema — TFormSchemaRenderer. `content` IS the SKILL.md body. */
export const skillFormSchema: FormSchemaItem[] = [
  { key: 'slug', labelKey: 'form.slug', label: 'Slug', type: 'text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'scope', labelKey: 'form.scope', label: 'Scope', type: 'select', options: skillScopeOptions },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'whenToUse', labelKey: 'form.whenToUse', label: 'When To Use', type: 'textarea' },
  { key: 'content', labelKey: 'form.content', label: 'Skill Content', type: 'textarea', required: true },
  { key: 'priority', labelKey: 'form.priority', label: 'Priority', type: 'number', min: 0, max: 1000 },
  { key: 'version', labelKey: 'form.version', label: 'Version', type: 'text' },
  { key: 'author', labelKey: 'form.author', label: 'Author', type: 'text' },
  { key: 'enabled', labelKey: 'form.enabled', label: 'Enabled', type: 'switch' },
]

/** Keyword search over name / slug / description. */
export const skillSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'search.keyword', label: 'Keyword', type: 'text' },
]

/** Truncate a multi-line description to a single-line card preview. */
export function describePreview(text: string | null | undefined, max = 110): string {
  const t = (text ?? '').replace(/\s+/g, ' ').trim()
  if (!t) return ''
  return t.length > max ? `${t.slice(0, max)}…` : t
}

/**
 * Flatten a recursive SkillCategoryDto tree into `{ label, value, depth }`
 * options for the toolbar category <select>. Indents child labels with a
 * leading spacer so the hierarchy reads in a flat dropdown without needing a
 * full NTree (keeps the toolbar compact). `skillCount` is appended when > 0.
 */
export interface CategoryOption {
  label: string
  value: string
  depth: number
}

export function flattenCategoryTree(
  nodes: Array<{ id: string; name: string; skillCount?: number; children?: unknown }> | null | undefined,
  depth = 0,
  acc: CategoryOption[] = [],
): CategoryOption[] {
  for (const node of nodes ?? []) {
    const pad = depth > 0 ? `${'  '.repeat(depth)}` : ''
    const count = node.skillCount && node.skillCount > 0 ? ` (${node.skillCount})` : ''
    acc.push({ label: `${pad}${node.name}${count}`, value: node.id, depth })
    flattenCategoryTree(
      (node.children as typeof nodes) ?? null,
      depth + 1,
      acc,
    )
  }
  return acc
}
