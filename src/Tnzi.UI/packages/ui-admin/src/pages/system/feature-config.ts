import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'

/**
 * Feature definition admin — backs Tnzi.Feature `/admin/feature-definitions`.
 * Fields mirror backend `FeatureDefinitionDto`:
 *   name (unique id) / displayName / description / defaultValue
 *   valueType: FeatureValueType enum   (0=Boolean / 1=Integer / 2=String)
 *   parentName (hierarchy) / isEnabled / group / source / isReadOnly
 *
 * `source = "Code"` rows come from IFeatureDefinitionProvider implementations
 * and are marked `isReadOnly`; backend rejects edit/delete on them.
 */
interface FeatureRow {
  id?: string
  name?: string
  displayName?: string
  description?: string
  defaultValue?: string
  valueType?: number
  parentName?: string
  isEnabled?: boolean
  group?: string
  source?: string
  isReadOnly?: boolean
}

function valueTypeLabel(v?: number): string {
  switch (v) {
    case 0: return 'Boolean'
    case 1: return 'Integer'
    case 2: return 'String'
    default: return '—'
  }
}

export const featureColumns: ColumnDef<FeatureRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 150 },
  { key: 'displayName', title: 'columns.displayName', minWidth: 150 },
  { key: 'group', title: 'columns.group', minWidth: 120 },
  {
    key: 'valueType',
    title: 'columns.valueType',
    width: 110,
    render: (row) => h('span', { style: 'font-family: monospace; font-size: 12px' }, valueTypeLabel(row.valueType)),
  },
  { key: 'defaultValue', title: 'columns.defaultValue', minWidth: 120 },
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
  {
    key: 'source',
    title: 'columns.source',
    width: 110,
    render: (row) => h(TSourceBadge, { value: String(row.source ?? 'Database') }),
  },
]

export const featureFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'displayName', labelKey: 'form.displayName', label: 'Display Name', type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'valueType',
    labelKey: 'form.valueType', label: 'Value Type',
    type: 'select',
    required: true,
    // Maps to FeatureValueType enum: 0=Boolean / 1=Integer / 2=String
    options: [
      { label: 'Boolean', value: 0 },
      { label: 'Integer', value: 1 },
      { label: 'String', value: 2 },
    ],
  },
  { key: 'defaultValue', labelKey: 'form.defaultValue', label: 'Default Value', type: 'text' },
  { key: 'parentName', labelKey: 'form.parentName', label: 'Parent Feature', type: 'text' },
  { key: 'group', labelKey: 'form.group', label: 'Group', type: 'text' },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
]
