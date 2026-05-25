import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Phase 5 Task 5.13 — PersonaList page config (sibling of PersonaList.vue).
 *
 * Shape derives from @tnzi/core/services/ai AgentPersonaDto /
 * CreateAgentPersonaDto / UpdateAgentPersonaDto.
 *
 * Plan deviations vs the task sketch:
 *   - The plan listed `avatarUrl` and `isEnabled` columns/fields, but the
 *     real DTO has neither. The persona shape is { name, slug, content,
 *     description, isSystem }. `content` is the system prompt body itself.
 *     Config follows backend reality (canonical-compact rule from Task 5.2).
 */
export const personaColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name' },
  { key: 'slug', title: 'columns.slug' },
  { key: 'description', title: 'columns.description' },
  { key: 'isSystem', title: 'columns.isSystem' },
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const personaFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'slug', labelKey: 'form.slug', label: 'Slug', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'content', labelKey: 'form.content', label: 'Persona Content', type: 'textarea', required: true },
  { key: 'isSystem', labelKey: 'form.isSystem', label: 'System Persona', type: 'switch' },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.persona.*):
 *
 * Page meta:
 *   modules.ai.persona.pageTitle
 *
 * Columns:
 *   modules.ai.persona.columns.name
 *   modules.ai.persona.columns.slug
 *   modules.ai.persona.columns.description
 *   modules.ai.persona.columns.isSystem
 *   modules.ai.persona.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.persona.form.name
 *   modules.ai.persona.form.slug
 *   modules.ai.persona.form.description
 *   modules.ai.persona.form.content
 *   modules.ai.persona.form.isSystem
 */
