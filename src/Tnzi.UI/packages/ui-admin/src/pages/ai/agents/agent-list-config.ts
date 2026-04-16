import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Phase 5 Task 5.2 — canonical AI page config (sibling of AgentList.vue).
 *
 * Pure data, no Vue components. Follows Phase 3 sibling-config convention
 * (see src/pages/_shared/page-conventions.md). All Phase 5 page tasks 5.3–5.14
 * use this file as their template — keep it small and dependency-free.
 *
 * Field shape derives from @tnzi/core/services/ai AgentDto / CreateAgentDto /
 * UpdateAgentDto. The form schema covers the create + edit superset; fields
 * that exist only on AgentDto (id, creationTime, lastModificationTime) are
 * surfaced via columns, not the form.
 */
export const agentColumns: ColumnDef[] = [
  { key: 'name', title: 'Name' },
  { key: 'description', title: 'Description' },
  { key: 'provider', title: 'Provider' },
  { key: 'model', title: 'Model' },
  { key: 'isEnabled', title: 'Enabled' },
  { key: 'qualityTier', title: 'Quality', visible: false },
  { key: 'latencyTier', title: 'Latency', visible: false },
  { key: 'costTier', title: 'Cost', visible: false },
  { key: 'lastModificationTime', title: 'Last Modified' },
]

export const agentProviderOptions: Array<{ label: string; value: string }> = [
  { label: 'OpenAI', value: 'openai' },
  { label: 'Anthropic', value: 'anthropic' },
  { label: 'Azure OpenAI', value: 'azure-openai' },
  { label: 'Ollama', value: 'ollama' },
  { label: 'DeepSeek', value: 'deepseek' },
]

export const agentFormSchema: FormSchemaItem[] = [
  { key: 'name', label: 'Name', type: 'text', required: true },
  { key: 'description', label: 'Description', type: 'textarea' },
  {
    key: 'provider',
    label: 'Provider',
    type: 'select',
    required: true,
    options: agentProviderOptions,
  },
  { key: 'model', label: 'Model', type: 'text' },
  { key: 'instructions', label: 'System Prompt', type: 'textarea' },
  { key: 'temperature', label: 'Temperature', type: 'number', min: 0, max: 2 },
  { key: 'maxTokens', label: 'Max Tokens', type: 'number', min: 1, max: 1000000 },
  { key: 'timeoutSeconds', label: 'Timeout (s)', type: 'number', min: 1, max: 3600 },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
]

/**
 * i18n keys referenced by AgentList.vue / agent-list-config.ts.
 * Phase 5 Task 5.16 owns the en/zh-cn locale expansion — the implementer of 5.16
 * should sweep this list into `tnzi.admin.modules.ai.agent.*`.
 *
 * Page meta:
 *   modules.ai.agent.pageTitle
 *
 * Columns:
 *   modules.ai.agent.columns.name
 *   modules.ai.agent.columns.description
 *   modules.ai.agent.columns.provider
 *   modules.ai.agent.columns.model
 *   modules.ai.agent.columns.isEnabled
 *   modules.ai.agent.columns.qualityTier
 *   modules.ai.agent.columns.latencyTier
 *   modules.ai.agent.columns.costTier
 *   modules.ai.agent.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.agent.form.name
 *   modules.ai.agent.form.description
 *   modules.ai.agent.form.provider
 *   modules.ai.agent.form.model
 *   modules.ai.agent.form.instructions
 *   modules.ai.agent.form.temperature
 *   modules.ai.agent.form.maxTokens
 *   modules.ai.agent.form.timeoutSeconds
 *   modules.ai.agent.form.isEnabled
 */
