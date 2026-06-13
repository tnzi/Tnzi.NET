import { h } from 'vue'
import { formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'

/**
 * Workflow definition list config (sibling of Workflows.vue).
 *
 * Backend shape: `WorkflowDefinitionDto` from @tnzi/core/services/ai —
 * `{ id, name, description?, steps[], executionMode, isEnabled,
 *    creationTime, lastModificationTime? }`.
 *
 * The form is intentionally narrow (name + description + executionMode +
 * isEnabled). The actual `steps[]` editing happens on the dedicated
 * editor page (WorkflowEditor.vue) — list-page create only sets the
 * scaffold, then bounces the user into the editor.
 */
export const workflowColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name', width: 200, fixed: 'left' },
  { key: 'description', title: 'columns.description' },
  {
    key: 'stepCount',
    title: 'columns.steps',
    width: 90,
    render: (row) => {
      const steps = (row as { steps?: unknown[] }).steps
      return String(Array.isArray(steps) ? steps.length : 0)
    },
  },
  {
    key: 'executionMode',
    title: 'columns.executionMode',
    width: 130,
    render: (row) => {
      // Backend serialises WorkflowExecutionMode as a numeric enum
      // (Sequential=0, Parallel=1, Dag=2) on the wire — accept either
      // the string name or the int and normalise to the string key the
      // mapping table uses.
      const raw = (row as { executionMode?: string | number }).executionMode
      let mode: string
      if (typeof raw === 'number') {
        mode = ['Sequential', 'Parallel', 'Dag'][raw] ?? 'Sequential'
      } else if (typeof raw === 'string') {
        mode = raw
      } else {
        mode = 'Sequential'
      }
      const map: Record<string, { type: 'info' | 'success' | 'warning' | 'default'; labelKey: string }> = {
        Sequential: { type: 'info', labelKey: 'admin.modules.ai.workflows.executionMode.sequential' },
        Parallel: { type: 'success', labelKey: 'admin.modules.ai.workflows.executionMode.parallel' },
        Dag: { type: 'warning', labelKey: 'admin.modules.ai.workflows.executionMode.dag' },
      }
      const entry = map[mode] ?? { type: 'default' as const, labelKey: '' }
      return h(TStatusBadge, {
        value: mode,
        type: entry.type,
        labelKey: entry.labelKey || undefined,
        label: entry.labelKey ? undefined : mode,
      })
    },
  },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean((row as { isEnabled?: boolean }).isEnabled),
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  {
    key: 'lastModificationTime',
    title: 'columns.lastModificationTime',
    width: 160,
    render: (row) =>
      formatDateTime(
        (row as { lastModificationTime?: string | null }).lastModificationTime,
        { fallback: '—' },
      ),
  },
]

/** Minimal create-form schema — `steps` is edited on WorkflowEditor.vue. */
export const workflowFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'executionMode',
    labelKey: 'admin.modules.ai.workflows.form.executionMode',
    label: 'Execution Mode',
    type: 'select',
    options: [
      { labelKey: 'admin.modules.ai.workflows.executionMode.sequential', label: 'Sequential', value: 'Sequential' },
      { labelKey: 'admin.modules.ai.workflows.executionMode.parallel', label: 'Parallel', value: 'Parallel' },
      { labelKey: 'admin.modules.ai.workflows.executionMode.dag', label: 'DAG', value: 'Dag' },
    ],
  },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]
