import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Phase 5 Task 5.8 — ProviderConfig page config (sibling of ProviderConfig.vue).
 *
 * Backed by entity-driven CRUD on AI_Provider table. Types come from
 * @tnzi/core/services/ai (ProviderDto / CreateProviderDto / UpdateProviderDto /
 * ProviderTestResultDto, added by Stage 3a backend prereq commit 84044010).
 *
 * API key tri-state semantic (per Stage 3a):
 *   - Create: apiKey is required (plaintext, encrypted at rest).
 *   - Update: null/omitted = keep existing cipher; '' = clear; non-empty = rotate.
 *   The form leaves apiKey blank by default in edit mode and uses the
 *   "(unchanged)" placeholder so the operator sees the keep-on-blank behavior.
 *
 * The provider type select uses a free-text input rather than a hard-coded
 * enum because backend accepts any registered provider key (OpenAI, Anthropic,
 * Azure, Ollama, plus custom plugins). A select would silently drop new
 * provider plugins from the UI.
 */
export const providerColumns: ColumnDef[] = [
  { key: 'name', title: 'Name' },
  { key: 'providerType', title: 'Type' },
  { key: 'endpoint', title: 'Endpoint' },
  { key: 'defaultModel', title: 'Default Model' },
  { key: 'priority', title: 'Priority' },
  { key: 'isEnabled', title: 'Enabled' },
  { key: 'hasApiKey', title: 'API Key Set' },
  { key: 'lastModificationTime', title: 'Last Modified' },
]

/**
 * Form schema is shared between create and update. Tri-state apiKey UX is
 * implemented in the page: edit mode injects placeholder "(unchanged)" and the
 * page-level toCreateDto/toUpdateDto adapters drop the apiKey when blank in
 * update mode (so the backend keeps the existing cipher).
 */
export const providerFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'Name', type: 'text', required: true },
  {
    key: 'providerType',
    label: 'Provider Type',
    type: 'text',
    required: true,
    placeholder: 'OpenAI / Anthropic / Azure / Ollama / ...',
  },
  { key: 'endpoint', label: 'Endpoint URL', type: 'text', placeholder: 'https://api.example.com/v1' },
  {
    key: 'apiKey',
    label: 'API Key',
    type: 'text',
    placeholder: 'Required on create; leave blank on edit to keep existing key',
  },
  { key: 'defaultModel', label: 'Default Model', type: 'text' },
  { key: 'priority', label: 'Priority', type: 'number', min: 0 },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'description', label: 'Description', type: 'textarea' },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.providers.*):
 *
 * Page meta:
 *   modules.ai.providers.pageTitle
 *   modules.ai.providers.testConnection
 *   modules.ai.providers.testing
 *   modules.ai.providers.testSuccess
 *   modules.ai.providers.testFailure
 *   modules.ai.providers.apiKeyHint
 *
 * Columns:
 *   modules.ai.providers.columns.name
 *   modules.ai.providers.columns.providerType
 *   modules.ai.providers.columns.endpoint
 *   modules.ai.providers.columns.defaultModel
 *   modules.ai.providers.columns.priority
 *   modules.ai.providers.columns.isEnabled
 *   modules.ai.providers.columns.hasApiKey
 *   modules.ai.providers.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.providers.form.name
 *   modules.ai.providers.form.providerType
 *   modules.ai.providers.form.endpoint
 *   modules.ai.providers.form.apiKey
 *   modules.ai.providers.form.defaultModel
 *   modules.ai.providers.form.priority
 *   modules.ai.providers.form.isEnabled
 *   modules.ai.providers.form.description
 */
