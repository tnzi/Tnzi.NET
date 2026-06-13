import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Providers page config (sibling of Providers.vue).
 *
 * Backed by entity-driven CRUD on the AI_Provider table. Types come from
 * @tnzi/core/services/ai (ProviderDto / CreateProviderDto / UpdateProviderDto /
 * ProviderTestResultDto). Providers render as a TCardPage grid — the card +
 * create/edit modal own presentation, so there are no table columns, only the
 * create/edit form schema + the keyword search field.
 *
 * API key tri-state semantic (per Stage 3a backend):
 *   - Create: apiKey is required (plaintext, encrypted at rest).
 *   - Update: blank entry = keep existing cipher; non-empty = rotate.
 *   The form leaves apiKey blank by default in edit mode and uses a
 *   "leave blank to keep" placeholder so the operator sees the keep-on-blank
 *   behavior. The page-level toCreateDto/toUpdateDto adapters drop the apiKey
 *   when blank in update mode so the backend keeps the existing cipher.
 *
 * The provider type uses a free-text input rather than a hard-coded enum
 * because the backend accepts any registered provider key (OpenAI, Anthropic,
 * Azure, Ollama, plus custom plugins). A select would silently drop new
 * provider plugins from the UI.
 */
export const providerFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'providerType',
    labelKey: 'form.providerType',
    label: 'Provider Type',
    type: 'text',
    required: true,
    placeholder: 'OpenAI / Anthropic / Azure / Ollama / ...',
  },
  {
    key: 'endpoint',
    labelKey: 'form.endpoint',
    label: 'Endpoint URL',
    type: 'text',
    placeholder: 'https://api.example.com/v1',
  },
  {
    key: 'apiKey',
    labelKey: 'form.apiKey',
    label: 'API Key',
    type: 'text',
    placeholder: 'Required on create; leave blank on edit to keep existing key',
  },
  { key: 'defaultModel', labelKey: 'form.defaultModel', label: 'Default Model', type: 'text' },
  { key: 'priority', labelKey: 'form.priority', label: 'Priority', type: 'number', min: 0 },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
]

/** Keyword search over name / provider type / endpoint / default model. */
export const providerSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'search.keyword', label: 'Keyword', type: 'text' },
]

/**
 * Map a provider name / type to an mdi icon. Matching is case-insensitive and
 * substring-based so e.g. "Azure OpenAI" resolves to the Azure glyph and an
 * "OpenAI Prod" name resolves to the OpenAI glyph. Falls back to a generic
 * robot glyph for unknown / custom provider plugins.
 */
export function providerIcon(provider: string | null | undefined): string {
  const key = (provider ?? '').toLowerCase()
  if (key.includes('azure')) return 'mdi:microsoft-azure'
  if (key.includes('anthropic') || key.includes('claude')) return 'mdi:robot-happy-outline'
  if (key.includes('openai') || key.includes('gpt')) return 'mdi:robot-outline'
  if (key.includes('ollama')) return 'mdi:llama'
  if (key.includes('google') || key.includes('gemini')) return 'mdi:google'
  if (key.includes('mistral')) return 'mdi:weather-windy'
  return 'mdi:robot-outline'
}
