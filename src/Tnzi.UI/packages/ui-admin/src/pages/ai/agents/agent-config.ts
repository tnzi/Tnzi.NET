import type { FormSchemaItem } from '../../_shared/form-schema'
import type { AgentDto, AgentExecutionMode, UpdateAgentDto } from '@tnzi/core/services/ai'

/**
 * AgentExecutionMode member names, mirrored locally as a const map.
 *
 * The page-layer ESLint guard (`no-restricted-imports`) forbids *value*
 * imports from `@tnzi/core/services/*` - only `import type` is allowed - and
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
 * presentation - there are no table columns, only the create/edit form schema,
 * the keyword + advanced search fields, and a few pure-data lookup maps the
 * card consumes (execution-mode labels, tier labels).
 *
 * useCrudPage is fed an empty `columns: []` by the page (the card slot owns
 * rendering), so this config no longer exports a `columns` table - only the
 * form schema, search fields, option lists, and label/type helpers.
 */

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
 * Map a tier to a coarse `tier.*` i18n key. The backend stores each tier on a
 * 1-5 scale (default 3): quality 5 = highest, latency 1 = fastest, cost 1 =
 * cheapest. The label states the value itself (1-2 low, 3 medium, 4-5 high),
 * so "Latency · High" means slow; {@link tierTagType} says whether that is good.
 * The raw number is still surfaced in the badge tooltip via the card.
 */
export function tierLabelKey(tier: number | null | undefined): string {
  const n = typeof tier === 'number' ? tier : 0
  if (n <= 2) return 'tier.low'
  if (n === 3) return 'tier.medium'
  return 'tier.high'
}

/**
 * Tag type for a tier badge. Quality is `'higherIsBetter'`; latency and cost
 * are `'lowerIsBetter'` (a high latency or cost tier is the warning).
 */
export function tierTagType(
  tier: number | null | undefined,
  direction: 'higherIsBetter' | 'lowerIsBetter',
): 'success' | 'info' | 'warning' {
  const bucket = tierLabelKey(tier)
  if (bucket === 'tier.medium') return 'info'
  const good = bucket === 'tier.high' ? direction === 'higherIsBetter' : direction === 'lowerIsBetter'
  return good ? 'success' : 'warning'
}

/** `CreateAgentDto` defaults, so the create form shows what an untouched field creates. */
export const agentCreateDefaults = {
  isEnabled: true,
  executionMode: ExecMode.Single,
} as Partial<AgentDto>

export const agentFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  // The configured provider's NAME, which is what the runtime resolves. Typed
  // here; the page turns it into a select over the configured providers when it
  // can read them. There is no built-in list: provider type slugs ("openai")
  // match a configured name only by coincidence.
  {
    key: 'provider',
    labelKey: 'form.provider',
    label: 'Provider',
    type: 'text',
    required: true,
    placeholderKey: 'form.providerPlaceholder',
    placeholder: 'Name of a configured provider',
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
 * The list Edit modal's update: the fields {@link agentFormSchema} shows, and
 * only those. A list row also carries tool, skill and knowledge grants, the
 * persona and the execution config; sent back from the modal they reverted
 * whatever the detail page, or another admin, changed after the list loaded.
 * A field the update leaves out is left as it is.
 */
export function toAgentUpdateDto(data: Partial<AgentDto>): UpdateAgentDto {
  const row = data as Record<string, unknown>
  return Object.fromEntries(agentFormSchema.map((f) => [f.key, row[f.key]])) as UpdateAgentDto
}

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
    type: 'text',
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
