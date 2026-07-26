import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Knowledge page config (sibling of Knowledge.vue).
 *
 * Knowledge bases render as a `TCardPage` grid - the card + the document
 * management drawer own all presentation, so there are no table columns, only
 * the create/edit form schema + the keyword search field.
 *
 * KB shape derives from `@tnzi/core/services/ai` `KnowledgeBaseDto`:
 *   { id, name, description, embeddingProvider, embeddingModel, chunkSize,
 *     chunkOverlap, documentCount, chunkCount, isEnabled, creationTime }
 *
 * Two distinct write shapes (see core `rag.ts`):
 *   - CREATE (`KnowledgeBaseCreateParams`): name + description +
 *     embeddingProvider/embeddingModel + chunkSize/chunkOverlap. The chunking
 *     config is immutable after creation (re-chunking needs a reindex), so it
 *     only appears in the create form.
 *   - UPDATE (`KnowledgeBaseUpdateParams`): only name / description / isEnabled
 *     are mutable. The edit form therefore disables the embedding + chunk
 *     fields and surfaces the enable toggle.
 *
 * The page passes the relevant schema depending on `mode` (create vs edit).
 */

/** Create form - full provisioning surface (KnowledgeBaseCreateParams). */
export const knowledgeCreateFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'embeddingProvider',
    labelKey: 'form.embeddingProvider',
    label: 'Embedding Provider',
    type: 'text',
    placeholder: 'e.g. openai',
  },
  {
    key: 'embeddingModel',
    labelKey: 'form.embeddingModel',
    label: 'Embedding Model',
    type: 'text',
    placeholder: 'e.g. text-embedding-3-small',
  },
  {
    key: 'chunkSize',
    labelKey: 'form.chunkSize',
    label: 'Chunk Size',
    type: 'number',
    placeholder: '1000',
  },
  {
    key: 'chunkOverlap',
    labelKey: 'form.chunkOverlap',
    label: 'Chunk Overlap',
    type: 'number',
    placeholder: '200',
  },
]

/** Edit form - only the mutable subset (KnowledgeBaseUpdateParams). */
export const knowledgeEditFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]

// The keyword search lives in the page-header bar (drives `query.searchText`,
// which the bridge forwards as the backend `keyword` param). A single-field
// advanced-search schema wrote `filters.keyword` - a key the bridge never read -
// so it was removed; the header search already covers keyword lookup.
