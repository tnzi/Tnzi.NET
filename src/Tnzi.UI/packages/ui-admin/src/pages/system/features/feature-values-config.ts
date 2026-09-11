import { h } from 'vue'
import { EMPTY_DASH } from '../../../utils/placeholders'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import { TSourceBadge } from '@tnzi/ui'
import type { FeatureValueWithDefinitionDto } from '../../../services/bridges/feature-bridge'

/**
 * One row of the per-scope value view - the backend DTO plus a typed `value`
 * seed for the edit form. The wire value is always a string; the form wants a
 * boolean for a switch and a number for a numeric input, so the tab converts on
 * the way in (`toFormValue`) and back (`toWireValue`).
 */
export interface FeatureValueRow extends FeatureValueWithDefinitionDto {
  value: boolean | number | string
}

/** Wire string → form control value, by the definition's value type. */
export function toFormValue(row: FeatureValueWithDefinitionDto): boolean | number | string {
  const raw = row.effectiveValue ?? ''
  switch (row.valueType) {
    case 'Boolean':
      return raw.toLowerCase() === 'true'
    case 'Integer': {
      const n = Number.parseInt(raw, 10)
      return Number.isFinite(n) ? n : 0
    }
    default:
      return raw
  }
}

/** Form control value → wire string. Booleans and numbers must not arrive as `undefined`. */
export function toWireValue(value: unknown, valueType: FeatureValueWithDefinitionDto['valueType']): string {
  switch (valueType) {
    case 'Boolean':
      return value === true || value === 'true' ? 'true' : 'false'
    case 'Integer':
      return String(typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : Number.parseInt(String(value ?? '0'), 10) || 0)
    default:
      return value == null ? '' : String(value)
  }
}

/** Badge tones for `effectiveSource`: explicit is the only one that is "this scope's own". */
export const effectiveSourceMapping = {
  Explicit: { type: 'success' as const, labelKey: 'admin.modules.system.features.values.source.Explicit' },
  Inherited: { type: 'info' as const, labelKey: 'admin.modules.system.features.values.source.Inherited' },
  Default: { type: 'default' as const, labelKey: 'admin.modules.system.features.values.source.Default' },
}

export const featureValueColumns: ColumnDef<FeatureValueRow>[] = [
  {
    key: 'featureName',
    title: 'values.columns.feature',
    minWidth: 200,
    render: (row) =>
      h('div', { class: 'fv-name' }, [
        h('span', { class: 'fv-name__display' }, row.displayName || row.featureName),
        h('code', { class: 'tnzi-mono text-11px fv-name__code' }, row.featureName),
      ]),
  },
  { key: 'group', title: 'values.columns.group', minWidth: 110 },
  {
    key: 'valueType',
    title: 'values.columns.valueType',
    width: 100,
    render: (row) => h('span', { class: 'tnzi-mono text-12px' }, row.valueType ?? EMPTY_DASH),
  },
  {
    key: 'defaultValue',
    title: 'values.columns.defaultValue',
    minWidth: 110,
    render: (row) => h('code', { class: 'tnzi-mono text-12px' }, row.defaultValue || EMPTY_DASH),
  },
  {
    key: 'effectiveValue',
    title: 'values.columns.effectiveValue',
    minWidth: 220,
    render: (row) =>
      h('div', { class: 'fv-effective' }, [
        h('code', { class: 'tnzi-mono text-12px fv-effective__value' }, row.effectiveValue || EMPTY_DASH),
        h(TStatusBadge, { value: row.effectiveSource, mapping: effectiveSourceMapping, size: 'small' }),
        row.effectiveProvider && row.effectiveSource === 'Inherited'
          ? h('span', { class: 'fv-effective__from text-12px' }, row.effectiveProvider)
          : null,
      ]),
  },
  {
    key: 'source',
    title: 'values.columns.source',
    width: 110,
    render: (row) => h(TSourceBadge, { value: String(row.source ?? 'Database') }),
  },
]

/**
 * The edit modal edits ONE thing: the scope's value. The control follows the
 * definition's type (`typeFn`), and the rest of the row is shown read-only as
 * context so the operator knows what they are overriding and what the current
 * answer would fall back to.
 */
export const featureValueFormSchema: FormSchemaItem[] = [
  {
    key: 'value',
    labelKey: 'values.form.value',
    label: 'Value',
    type: 'text',
    typeFn: (model) => {
      switch (model.valueType) {
        case 'Boolean': return 'switch'
        case 'Integer': return 'number'
        default: return 'text'
      }
    },
    required: true,
    hintKey: 'values.form.valueHint',
    hint: 'Applies to the selected scope only.',
  },
]
