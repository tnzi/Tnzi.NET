import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Phase 5 Task 5.10 — KbManager page config (sibling of KbManager.vue).
 *
 * Bridge contract: `bridge.knowledge` is typed as `Record<string, unknown>`
 * because the backend KnowledgeBase DTO is not exported from
 * @tnzi/core/services/ai (rag.ts only exposes `KnowledgeBaseCreateParams` /
 * `KnowledgeBaseUpdateParams` / `ReindexResultDto`). Columns/form fields
 * follow the create/update params shape — name + description + embeddingModel
 * — plus the audit fields the entity is known to carry. If a future task
 * exports a typed `KnowledgeBaseDto`, swap in the proper type here.
 */
export const kbColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name' },
  { key: 'description', title: 'columns.description' },
  { key: 'embeddingModel', title: 'columns.embeddingModel' },
  { key: 'chunkCount', title: 'columns.chunkCount', visible: false },
  { key: 'documentCount', title: 'columns.documentCount', visible: false },
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const kbFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'Name', type: 'text', required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  { key: 'embeddingModel', label: 'Embedding Model', type: 'text' },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.knowledge.*):
 *
 * Page meta:
 *   modules.ai.knowledge.pageTitle
 *   modules.ai.knowledge.reindex
 *   modules.ai.knowledge.reindexing
 *   modules.ai.knowledge.reindexSuccess
 *   modules.ai.knowledge.reindexError
 *
 * Columns:
 *   modules.ai.knowledge.columns.name
 *   modules.ai.knowledge.columns.description
 *   modules.ai.knowledge.columns.embeddingModel
 *   modules.ai.knowledge.columns.chunkCount
 *   modules.ai.knowledge.columns.documentCount
 *   modules.ai.knowledge.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.knowledge.form.name
 *   modules.ai.knowledge.form.description
 *   modules.ai.knowledge.form.embeddingModel
 */
