import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

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
  { key: 'name', title: 'Name' },
  { key: 'slug', title: 'Slug' },
  { key: 'scope', title: 'Scope' },
  { key: 'description', title: 'Description' },
  { key: 'priority', title: 'Priority', visible: false },
  { key: 'version', title: 'Version', visible: false },
  { key: 'author', title: 'Author', visible: false },
  { key: 'enabled', title: 'Enabled' },
  { key: 'source', title: 'Source', visible: false },
  { key: 'lastModificationTime', title: 'Last Modified' },
]

export const skillScopeOptions: Array<{ label: string; value: string }> = [
  { label: 'User', value: 'User' },
  { label: 'Project', value: 'Project' },
  { label: 'Global', value: 'Global' },
]

export const skillFormSchema: FormSchemaItem[] = [
  { key: 'slug', label: 'Slug', type: 'text', required: true },
  { key: 'name', label: 'Name', type: 'text', required: true },
  { key: 'scope', label: 'Scope', type: 'select', options: skillScopeOptions },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'whenToUse', label: 'When To Use', type: 'textarea' },
  { key: 'content', label: 'Skill Content', type: 'textarea', required: true },
  { key: 'priority', label: 'Priority', type: 'number', min: 0, max: 1000 },
  { key: 'version', label: 'Version', type: 'text' },
  { key: 'author', label: 'Author', type: 'text' },
  { key: 'enabled', label: 'Enabled', type: 'switch' },
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
