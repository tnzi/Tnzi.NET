import { h } from 'vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'

/**
 * Phase 5 Task 5.2 — canonical AI page config (sibling of Agents.vue).
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
  { key: 'name', title: 'columns.name' },
  { key: 'description', title: 'columns.description' },
  { key: 'provider', title: 'columns.provider' },
  { key: 'model', title: 'columns.model' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  { key: 'qualityTier', title: 'columns.qualityTier', visible: false },
  { key: 'latencyTier', title: 'columns.latencyTier', visible: false },
  { key: 'costTier', title: 'columns.costTier', visible: false },
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const agentProviderOptions: Array<{ label: string; value: string }> = [
  { label: 'OpenAI', value: 'openai' },
  { label: 'Anthropic', value: 'anthropic' },
  { label: 'Azure OpenAI', value: 'azure-openai' },
  { label: 'Ollama', value: 'ollama' },
  { label: 'DeepSeek', value: 'deepseek' },
]

export const agentFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'provider',
    labelKey: 'form.provider', label: 'Provider',
    type: 'select',
    required: true,
    options: agentProviderOptions,
  },
  { key: 'model', labelKey: 'form.model', label: 'Model', type: 'text' },
  { key: 'instructions', labelKey: 'form.instructions', label: 'System Prompt', type: 'textarea' },
  { key: 'temperature', labelKey: 'form.temperature', label: 'Temperature', type: 'number', min: 0, max: 2 },
  { key: 'maxTokens', labelKey: 'form.maxTokens', label: 'Max Tokens', type: 'number', min: 1, max: 1000000 },
  { key: 'timeoutSeconds', labelKey: 'form.timeoutSeconds', label: 'Timeout (s)', type: 'number', min: 1, max: 3600 },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]

/**
 * i18n keys referenced by Agents.vue / agent-config.ts.
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
