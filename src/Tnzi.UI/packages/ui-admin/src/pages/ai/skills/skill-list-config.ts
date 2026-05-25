import { h } from 'vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'

/**
 * Phase 5 Task 5.7 — SkillList page config (sibling of SkillList.vue).
 *
 * Shape derives from @tnzi/core/services/ai SkillSummaryDto / CreateSkillDto /
 * UpdateSkillDto. The summary DTO is what the paged list endpoint returns;
 * fields like `parameters`, `allowedToolGroups`, etc. live on SkillDetailDto
 * and are out of scope for the canonical CRUD page (they get a dedicated
 * editor in a later phase).
 *
 * Note: backend uses `enabled` (not `isEnabled`) on skill DTOs.
 */
export const skillColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name', width: 180 },
  { key: 'slug', title: 'columns.slug', width: 160 },
  { key: 'scope', title: 'columns.scope', width: 100 },
  { key: 'description', title: 'columns.description', ellipsis: { tooltip: true } },
  { key: 'priority', title: 'columns.priority', visible: false },
  { key: 'version', title: 'columns.version', visible: false },
  { key: 'author', title: 'columns.author', visible: false },
  {
    key: 'enabled',
    title: 'columns.enabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.enabled),
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  {
    key: 'source',
    title: 'columns.source',
    width: 110,
    render: (row) =>
      h(TSourceBadge, {
        // SkillSource enum is serialised as numeric (System.Text.Json default);
        // TSourceBadge accepts number | string and maps ordinals correctly.
        value: (row.source ?? 1) as number | string,
      }),
  },
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const skillScopeOptions: Array<{ label: string; value: string }> = [
  { label: 'User', value: 'User' },
  { label: 'Project', value: 'Project' },
  { label: 'Global', value: 'Global' },
]

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

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.skill.*):
 *
 * Page meta:
 *   modules.ai.skill.pageTitle
 *
 * Columns:
 *   modules.ai.skill.columns.name
 *   modules.ai.skill.columns.slug
 *   modules.ai.skill.columns.scope
 *   modules.ai.skill.columns.description
 *   modules.ai.skill.columns.priority
 *   modules.ai.skill.columns.version
 *   modules.ai.skill.columns.author
 *   modules.ai.skill.columns.enabled
 *   modules.ai.skill.columns.source
 *   modules.ai.skill.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.skill.form.slug
 *   modules.ai.skill.form.name
 *   modules.ai.skill.form.scope
 *   modules.ai.skill.form.description
 *   modules.ai.skill.form.whenToUse
 *   modules.ai.skill.form.content
 *   modules.ai.skill.form.priority
 *   modules.ai.skill.form.version
 *   modules.ai.skill.form.author
 *   modules.ai.skill.form.enabled
 */
