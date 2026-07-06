/**
 * `TSchemaForm` — declarative schema-driven form renderer.
 *
 * Walks a `FormSchemaItem[]` and renders each field as the matching
 * naive-ui control (NInput / NInputNumber / NSwitch / NSelect /
 * NDatePicker). Supports single-column (`columns = 1`) and grid
 * (`columns = 2..n`) layouts, readonly view-mode, and an injected
 * `translate` function for i18n.
 *
 * Sunk from `@tnzi/ui-admin/pages/_shared/form-schema.ts` in 0.2.x so
 * site / chat / mobile and any consumer can use the same form-builder
 * shape. The companion CSS (`.t-form-schema--compact`) lives in
 * `./form-schema.css` and must be imported by the application shell
 * (typically through the package's `style.css` aggregate).
 */
import { defineComponent, h, type PropType, type VNodeChild } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect, NDatePicker } from 'naive-ui'
import './form-schema.css'

export type FormSchemaBuiltinFieldType = 'text' | 'textarea' | 'number' | 'switch' | 'select' | 'date'
/**
 * Builtin field types render to naive-ui controls. Any other string is a
 * custom field type rendered through the `fieldRenderers` prop — this keeps
 * autocomplete for the builtins while allowing extension without forking.
 */
export type FormSchemaFieldType = FormSchemaBuiltinFieldType | (string & {})

export interface FormSchemaItem {
  key: string
  /**
   * Field label. Can be:
   *  - a raw string (displayed verbatim — legacy behaviour), or
   *  - an i18n key (e.g. `'form.name'` or `'admin.modules.xxx.form.name'`)
   *    when `translate` is provided.
   * When `labelKey` is set it takes precedence over `label`.
   */
  label: string
  /**
   * Short i18n key for the field label. Resolved through the renderer's
   * `translate` prop.
   */
  labelKey?: string
  type: FormSchemaFieldType
  // When present, takes precedence over `type` and lets the field switch
  // its editor based on other model values (e.g. SettingValueType-aware
  // value editor: text/number/switch/textarea per sibling enum).
  typeFn?: (model: Record<string, unknown>) => FormSchemaFieldType
  required?: boolean
  placeholder?: string
  /** Optional i18n key for the placeholder. */
  placeholderKey?: string
  options?: Array<{ label: string; value: string | number; labelKey?: string }>
  visible?: (model: Record<string, unknown>) => boolean
  max?: number
  min?: number
  /**
   * How many grid columns this field spans in a multi-column form layout.
   * Default behaviour (when `columns > 1` on the renderer):
   *   - `textarea` fields auto-span the full row (`= columns`)
   *   - all other fields default to span 1
   * Set explicitly to override (e.g. a long `text` field that should span
   * 2 columns inside a 3-column form). Ignored when the renderer is in
   * single-column mode (`columns === 1`).
   */
  span?: number
}

/**
 * Context handed to a custom field renderer registered via `fieldRenderers`.
 */
export interface FieldRenderContext {
  item: FormSchemaItem
  value: unknown
  readonly: boolean
  onUpdate: (v: unknown) => void
  /** Resolve an i18n key to text, with a fallback for raw strings. */
  translate: (key: string | undefined, fallback: string) => string
}

/**
 * Renders a single field for a given (possibly custom) field `type`.
 * Register under the `fieldRenderers` prop to extend TSchemaForm with custom
 * editors (e.g. markdown / color) without forking the component.
 */
export type FieldRenderer = (ctx: FieldRenderContext) => VNodeChild

interface Props {
  schema: FormSchemaItem[]
  model: Record<string, unknown>
  readonly: boolean
  translate?: (key: string) => string
  columns?: number
  /**
   * Custom field renderers keyed by field `type`. A field whose `type`
   * matches a key here is rendered by that function instead of a builtin
   * control — the extension point for markdown/color/etc. without forking.
   */
  fieldRenderers?: Record<string, FieldRenderer>
}

/**
 * date 字段值兼容：编辑回填的模型里日期常是后端 ISO 字符串（date-only = UTC 午夜），
 * 而 NDatePicker 只接受 number 时间戳。字符串按**日历日**解析为本地午夜时间戳——
 * 直接 `new Date(iso).getTime()` 会让 UTC 以西时区显示成前一天。
 */
function toDateTimestamp(value: unknown): number | null {
  if (typeof value === 'number') return value
  if (typeof value === 'string' && value.length >= 10) {
    const y = Number(value.slice(0, 4))
    const m = Number(value.slice(5, 7))
    const d = Number(value.slice(8, 10))
    if (Number.isFinite(y) && Number.isFinite(m) && Number.isFinite(d) && m >= 1 && m <= 12) {
      return new Date(y, m - 1, d).getTime()
    }
  }
  return null
}

const TSchemaForm = defineComponent({
  name: 'TSchemaForm',
  props: {
    schema: { type: Array as PropType<FormSchemaItem[]>, required: true },
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    readonly: { type: Boolean, default: false },
    translate: { type: Function as PropType<(key: string) => string>, default: undefined },
    /**
     * Number of columns the form grid renders at. Default `1` keeps the
     * legacy single-column layout (back-compat for simple forms).
     * Pass `2` or `3` on dense-field pages (≥6 fields) to fold the form
     * into a multi-column grid — the host modal must also widen itself
     * so the columns don't crush each other.
     */
    columns: { type: Number, default: 1 },
    fieldRenderers: { type: Object as PropType<Record<string, FieldRenderer>>, default: undefined },
  },
  setup(props: Props) {
    function tr(key: string | undefined, fallback: string): string {
      if (!key) return fallback
      if (!props.translate) return fallback
      const out = props.translate(key)
      // When the key is unresolved, callers' translators typically return
      // a humanised fallback — that's still better than the raw key, but
      // for raw string labels we want the user-supplied string to win.
      return out || fallback
    }

    function renderField(item: FormSchemaItem) {
      // View-mode UX: text-style fields (input / textarea / number) use the
      // native `readonly` prop so the field keeps normal text colour and only
      // blocks editing — `disabled` would grey-out the body, which made long
      // content (e.g. 6KB template bodies) unreadable. Non-text widgets
      // (select / switch / date) don't expose `readonly`, so we still use
      // `disabled` but the `.t-form-schema--compact` CSS overrides their
      // muted colour back to normal text. End result: every field looks
      // identical to edit mode, just non-interactive.
      const viewMode = props.readonly
      const value = props.model[item.key]
      const onUpdate = (v: unknown) => { props.model[item.key] = v }
      const effectiveType = item.typeFn ? item.typeFn(props.model) : item.type
      // A custom renderer registered for this (possibly non-builtin) type wins.
      const custom = props.fieldRenderers?.[effectiveType]
      if (custom) {
        return custom({ item, value, readonly: viewMode, onUpdate, translate: tr })
      }
      const placeholder = tr(item.placeholderKey, item.placeholder ?? '')
      switch (effectiveType) {
        case 'text':
          return h(NInput, {
            value: value as string | null,
            readonly: viewMode,
            placeholder,
            'onUpdate:value': onUpdate,
          })
        case 'textarea':
          // autosize bounds keep large field values (e.g. 6KB template
          // bodies) from blowing past the modal viewport — without them,
          // a long textarea pushes sibling fields off-screen and the
          // modal's vertical overflow doesn't kick in because the form
          // grid has no intrinsic height limit.
          return h(NInput, {
            value: value as string | null,
            readonly: viewMode,
            type: 'textarea',
            placeholder,
            autosize: { minRows: 3, maxRows: 14 },
            'onUpdate:value': onUpdate,
          })
        case 'number':
          return h(NInputNumber, {
            value: value as number | null,
            readonly: viewMode,
            min: item.min,
            max: item.max,
            'onUpdate:value': onUpdate,
          })
        case 'switch':
          return h(NSwitch, { value: value as boolean, disabled: viewMode, 'onUpdate:value': onUpdate })
        case 'select': {
          const opts = (item.options ?? []).map((o) => ({
            value: o.value,
            label: tr(o.labelKey, o.label),
          }))
          return h(NSelect, { value: value as string | number | null, disabled: viewMode, options: opts, 'onUpdate:value': onUpdate })
        }
        case 'date':
          return h(NDatePicker, { value: toDateTimestamp(value), disabled: viewMode, type: 'date', 'onUpdate:value': onUpdate })
        default:
          // Unknown field type without a registered renderer: degrade to a
          // visible read-only text rendering rather than silently rendering
          // nothing — the surfaced value signals the missing editor to the dev.
          return h('span', { class: 't-form-schema__unknown' }, String(value ?? ''))
      }
    }

    // Resolve the effective column span for a single field. Single-column
    // mode (`columns === 1`) collapses every field to a full row; multi-
    // column mode honours an explicit `item.span`, defaults textareas to
    // the full row, and everything else to 1 column.
    function fieldSpan(item: FormSchemaItem): number {
      const cols = props.columns ?? 1
      if (cols <= 1) return 1
      if (typeof item.span === 'number' && item.span > 0) {
        return Math.min(item.span, cols)
      }
      const effectiveType = item.typeFn ? item.typeFn(props.model) : item.type
      if (effectiveType === 'textarea') return cols
      return 1
    }

    return () => {
      const cols = props.columns ?? 1
      const multiCol = cols > 1
      const items = props.schema.filter((item) => !item.visible || item.visible(props.model))
      // Visual tightening (label-padding shrink, feedback-block compression,
      // row-margin tweak) is handled by the `.t-form-schema--compact` CSS
      // class — see `./form-schema.css`. Validation feedback (incl. the
      // required-field marker `*` and inline `rule` errors) stays enabled
      // so destructive submits don't silently swallow validation failures.
      const formChildren = items.map((item) => {
        const span = fieldSpan(item)
        const itemStyle = multiCol ? `grid-column: span ${span};` : undefined
        return h(
          NFormItem,
          {
            label: tr(item.labelKey, item.label),
            path: item.key,
            required: item.required,
            key: item.key,
            style: itemStyle,
          },
          { default: () => renderField(item) },
        )
      })
      const formStyle = multiCol
        ? {
            display: 'grid',
            gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
            columnGap: '16px',
            rowGap: '0',
          }
        : undefined
      return h(
        NForm,
        {
          // The marker class drives compact spacing via ./form-schema.css.
          // Applied unconditionally so single-column forms benefit too — the
          // pre-refactor naive-ui defaults (24px feedback block, 8px label
          // padding) were always too loose for admin CRUD modals.
          class: 't-form-schema--compact',
          style: formStyle,
          labelPlacement: multiCol ? 'top' : undefined,
        } as Record<string, unknown>,
        { default: () => formChildren },
      )
    }
  },
})

export default TSchemaForm
