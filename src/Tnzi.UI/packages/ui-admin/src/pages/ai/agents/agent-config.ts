import type { FormSchemaItem } from '../../_shared/form-schema'
import type { AgentExecutionMode } from '@tnzi/core/services/ai'

/**
 * AgentExecutionMode member names, mirrored locally as a const map.
 *
 * The page-layer ESLint guard (`no-restricted-imports`) forbids *value*
 * imports from `@tnzi/core/services/*` — only `import type` is allowed — and
 * the ai-bridge does not re-export this enum. The backend serializes the enum
 * as its PascalCase member NAME (global JsonStringEnumConverter), so this local
 * mirror uses the name strings; the `AgentExecutionMode` *type* is still
 * imported for signatures.
 */
const ExecMode = {
  Single: 'Single',
  Handoff: 'Handoff',
  AgentAsTools: 'AgentAsTools',
  Router: 'Router',
} as const

/**
 * Agents page config (sibling of Agents.vue).
 *
 * Field shape derives from @tnzi/core/services/ai AgentDto / CreateAgentDto /
 * UpdateAgentDto. Agents render as a production-grade TCardPage grid (tier
 * badges + health KPI strip + clone + drill-in detail), so the card owns
 * presentation — there are no table columns, only the create/edit form schema,
 * the keyword + advanced search fields, and a few pure-data lookup maps the
 * card consumes (execution-mode labels, tier labels).
 *
 * useCrudPage is fed an empty `columns: []` by the page (the card slot owns
 * rendering), so this config no longer exports a `columns` table — only the
 * form schema, search fields, option lists, and label/type helpers.
 */

export const agentProviderOptions: Array<{ label: string; value: string }> = [
  { label: 'OpenAI', value: 'openai' },
  { label: 'Anthropic', value: 'anthropic' },
  { label: 'Azure OpenAI', value: 'azure-openai' },
  { label: 'Ollama', value: 'ollama' },
  { label: 'DeepSeek', value: 'deepseek' },
]

/**
 * Execution-mode options for the form select + advanced-search filter.
 * Values are the AgentExecutionMode member names (what the backend serializes);
 * `labelKey` resolves through translatePageKey('ai.agents', …) → `mode.*`.
 */
export const agentExecutionModeOptions: Array<{ label: string; labelKey: string; value: string }> = [
  { value: ExecMode.Single, labelKey: 'mode.single', label: 'Single' },
  { value: ExecMode.Handoff, labelKey: 'mode.handoff', label: 'Handoff' },
  { value: ExecMode.AgentAsTools, labelKey: 'mode.agentAsTools', label: 'Agent-as-tools' },
  { value: ExecMode.Router, labelKey: 'mode.router', label: 'Router' },
]

/**
 * Map an AgentExecutionMode value to a `mode.*` i18n key. Used by the card to
 * label the execution-mode badge. Accepts the serialized member name; a legacy
 * numeric value is normalized defensively. Unknown values → `mode.unknown`.
 */
export function executionModeKey(mode: AgentExecutionMode | number | string | null | undefined): string {
  const name =
    typeof mode === 'number' ? ['Single', 'Handoff', 'AgentAsTools', 'Router'][mode] : mode
  switch (name) {
    case ExecMode.Single:
      return 'mode.single'
    case ExecMode.Handoff:
      return 'mode.handoff'
    case ExecMode.AgentAsTools:
      return 'mode.agentAsTools'
    case ExecMode.Router:
      return 'mode.router'
    default:
      return 'mode.unknown'
  }
}

/**
 * Map a 0..N tier number to a coarse `tier.*` i18n key. The backend stores
 * tiers as small integers (higher = more quality / lower latency / lower cost,
 * per the framework's tier semantics); the card collapses them to low/medium/
 * high buckets so the badge text stays human-readable. The raw number is still
 * surfaced in the badge tooltip via the card.
 */
export function tierLabelKey(tier: number | null | undefined): string {
  const n = typeof tier === 'number' ? tier : 0
  if (n <= 1) return 'tier.low'
  if (n === 2) return 'tier.medium'
  return 'tier.high'
}

/** Naive tag type for a tier badge — low=warning, medium=info, high=success. */
export function tierTagType(tier: number | null | undefined): 'success' | 'info' | 'warning' {
  const n = typeof tier === 'number' ? tier : 0
  if (n <= 1) return 'warning'
  if (n === 2) return 'info'
  return 'success'
}

export const agentFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'provider',
    labelKey: 'form.provider',
    label: 'Provider',
    type: 'select',
    required: true,
    options: agentProviderOptions,
  },
  { key: 'model', labelKey: 'form.model', label: 'Model', type: 'text' },
  {
    key: 'executionMode',
    labelKey: 'form.executionMode',
    label: 'Execution Mode',
    type: 'select',
    options: agentExecutionModeOptions,
  },
  { key: 'instructions', labelKey: 'form.instructions', label: 'System Prompt', type: 'textarea' },
  { key: 'temperature', labelKey: 'form.temperature', label: 'Temperature', type: 'number', min: 0, max: 2 },
  { key: 'maxTokens', labelKey: 'form.maxTokens', label: 'Max Tokens', type: 'number', min: 1, max: 1000000 },
  { key: 'timeoutSeconds', labelKey: 'form.timeoutSeconds', label: 'Timeout (s)', type: 'number', min: 1, max: 3600 },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]

/**
 * Search fields: keyword (free text → bridge keyword) + optional provider /
 * execution-mode advanced filters. The shell writes `keyword` into
 * `query.searchText` and the rest into `query.filters`, which the agents bridge
 * forwards to the backend list query.
 */
export const agentSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'search.keyword', label: 'Keyword', type: 'text' },
  {
    key: 'provider',
    labelKey: 'search.provider',
    label: 'Provider',
    type: 'select',
    options: agentProviderOptions,
    placeholderKey: 'search.providerPlaceholder',
  },
  {
    key: 'executionMode',
    labelKey: 'search.executionMode',
    label: 'Execution Mode',
    type: 'select',
    options: agentExecutionModeOptions,
    placeholderKey: 'search.executionModePlaceholder',
  },
]
