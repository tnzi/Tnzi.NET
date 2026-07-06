import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'

/**
 * Feature definition admin — backs Tnzi.Feature `/admin/feature-definitions`.
 * Fields mirror backend `FeatureDefinitionDto`:
 *   name (unique id) / displayName / description / defaultValue
 *   valueType: FeatureValueType enum — serialized by the backend's global
 *     JsonStringEnumConverter as its member name: "Boolean" | "Integer" | "String"
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
  valueType?: string
  parentName?: string
  isEnabled?: boolean
  group?: string
  source?: string
  isReadOnly?: boolean
}

export const featureColumns: ColumnDef<FeatureRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 150 },
  { key: 'displayName', title: 'columns.displayName', minWidth: 150 },
  { key: 'group', title: 'columns.group', minWidth: 120 },
  {
    key: 'valueType',
    title: 'columns.valueType',
    width: 110,
    // valueType is already the human-readable member name ("Boolean" / "Integer"
    // / "String"), so it renders verbatim.
    render: (row) => h('span', { class: 'tnzi-mono text-12px' }, row.valueType ?? '—'),
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
    // FeatureValueType enum member names (JsonStringEnumConverter wire shape).
    options: [
      { label: 'Boolean', value: 'Boolean' },
      { label: 'Integer', value: 'Integer' },
      { label: 'String', value: 'String' },
    ],
  },
  { key: 'defaultValue', labelKey: 'form.defaultValue', label: 'Default Value', type: 'text' },
  { key: 'parentName', labelKey: 'form.parentName', label: 'Parent Feature', type: 'text' },
  { key: 'group', labelKey: 'form.group', label: 'Group', type: 'text' },
  // isEnabled only exists on the update path — CreateFeatureDto has no such
  // field, so hide the toggle in create mode (model has no id yet).
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch', visible: (model) => !!model.id },
]
