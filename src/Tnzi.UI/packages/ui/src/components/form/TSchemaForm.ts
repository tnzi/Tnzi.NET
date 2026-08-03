/**
 * `TSchemaForm` - declarative schema-driven form renderer.
 *
 * Walks a `FormSchemaItem[]` and renders each field as the matching
 * naive-ui control (NInput / NInputNumber / NSwitch / NSelect /
 * NDatePicker). Supports single-column, fixed multi-column and
 * container-derived (`columns: 'auto'`) layouts, **titled sections**,
 * readonly view-mode (as inputs or as a description list), and an injected
 * `translate` function for i18n.
 *
 * Sunk from `@tnzi/ui-admin/pages/_shared/form-schema.ts` in 0.2.x so
 * site / chat / mobile and any consumer can use the same form-builder
 * shape. The companion CSS (`.t-form-schema--compact`) lives in
 * `./form-schema.css` and must be imported by the application shell
 * (typically through the package's `style.css` aggregate).
 */
import { computed, defineComponent, h, type PropType, type VNode, type VNodeChild } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect, NDatePicker, NTag } from 'naive-ui'
import { useBreakpoints } from '../../headless/theme/useBreakpoints'
import TDescriptions, { type DescriptionItem } from '../display/TDescriptions.vue'
import TSvgIcon from '../display/TSvgIcon.vue'
import { EMPTY_DASH, isEmptyValue } from '../../utils/placeholders'
import './form-schema.css'

export type FormSchemaBuiltinFieldType = 'text' | 'textarea' | 'number' | 'switch' | 'select' | 'date'
/**
 * Builtin field types render to naive-ui controls. Any other string is a
 * custom field type rendered through the `fieldRenderers` prop - this keeps
 * autocomplete for the builtins while allowing extension without forking.
 */
export type FormSchemaFieldType = FormSchemaBuiltinFieldType | (string & {})

export interface FormSchemaItem {
  key: string
  /**
   * Field label. Can be:
   *  - a raw string (displayed verbatim - legacy behaviour), or
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
   * Default behaviour (when the renderer is in a multi-column mode):
   *   - `textarea` fields auto-span the full row
   *   - all other fields default to span 1
   *
   * In fixed-column mode (`columns: 2` / `3` / …) a numeric span is honoured
   * exactly. In container-derived mode (`columns: 'auto'`) the column count is
   * decided by the browser, so anything wider than one column claims the whole
   * row instead - a numeric span cannot be honoured against an unknown track
   * count without risking overflow when the grid folds to one column.
   */
  span?: number | 'full'
  /**
   * Key of the {@link FormSchemaSection} this field belongs to. Fields with no
   * `section` (or one that matches no declared section) render in an untitled
   * leading block, so existing flat schemas are unaffected.
   */
  section?: string
  /** Optional one-line help text under the control. */
  hint?: string
  /** i18n key for {@link hint}. */
  hintKey?: string
}

/**
 * A titled block of fields. Sections turn a wall of inputs into a document
 * with headings ("Identity", "Contact", "Banking") - the single biggest
 * readability win for records with more than ~6 fields.
 */
export interface FormSchemaSection {
  /** Matches {@link FormSchemaItem.section}. */
  key: string
  /** Heading text, or the fallback when `labelKey` misses. Omit for a silent
   *  grouping (fields grouped for layout, no visible heading). */
  label?: string
  /** i18n key for the heading. */
  labelKey?: string
  /** One-line explanation under the heading. */
  hint?: string
  /** i18n key for the hint. */
  hintKey?: string
  /** Iconify name shown before the heading. */
  icon?: string
  /** Per-section column override. Falls back to the renderer's `columns`. */
  columns?: number | 'auto'
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

/**
 * How a readonly form renders.
 *  - `inputs` - the legacy shape: the same controls, non-interactive.
 *  - `descriptions` - `label: value` rows (a {@link TDescriptions} list). Reads
 *    as a record rather than as a disabled form, and is the right default for
 *    a "view" action.
 */
export type ReadonlyLayout = 'inputs' | 'descriptions'

interface Props {
  schema: FormSchemaItem[]
  model: Record<string, unknown>
  readonly: boolean
  translate?: (key: string) => string
  columns?: number | 'auto'
  sections?: FormSchemaSection[]
  readonlyLayout?: ReadonlyLayout
  /**
   * Custom field renderers keyed by field `type`. A field whose `type`
   * matches a key here is rendered by that function instead of a builtin
   * control - the extension point for markdown/color/etc. without forking.
   */
  fieldRenderers?: Record<string, FieldRenderer>
  /** Minimum column width in `columns: 'auto'` mode. */
  minColumnWidth?: number
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

/** ISO calendar date for a description row - stable across locales/timezones. */
function toDateText(value: unknown): string {
  const ts = toDateTimestamp(value)
  if (ts === null) return ''
  const d = new Date(ts)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

const TSchemaForm = defineComponent({
  name: 'TSchemaForm',
  props: {
    schema: { type: Array as PropType<FormSchemaItem[]>, required: true },
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    readonly: { type: Boolean, default: false },
    translate: { type: Function as PropType<(key: string) => string>, default: undefined },
    /**
     * Column layout:
     *   - `1` (default) keeps the legacy single-column form.
     *   - `2` / `3` / … render a fixed grid; a field's numeric `span` is exact.
     *   - `'auto'` lets the CONTAINER decide (`repeat(auto-fit, minmax(...))`),
     *     which is correct inside modals/drawers/panels of unknown width - a
     *     560px modal folds to 2 columns and a 420px drawer to 1 with no
     *     breakpoint wiring at the call site.
     */
    columns: { type: [Number, String] as PropType<number | 'auto'>, default: 1 },
    /** Titled field blocks. Fields opt in via `FormSchemaItem.section`. */
    sections: { type: Array as PropType<FormSchemaSection[]>, default: undefined },
    /** Shape of the readonly rendering. See {@link ReadonlyLayout}. */
    readonlyLayout: { type: String as PropType<ReadonlyLayout>, default: 'inputs' },
    fieldRenderers: { type: Object as PropType<Record<string, FieldRenderer>>, default: undefined },
    minColumnWidth: { type: Number, default: 240 },
  },
  setup(props: Props) {
    // 手机端（<768px）强制单列：`:columns="2"` 等多列表单在窄屏会把每个
    // 字段挤成一条缝（消费方 BankFeed/Receipts 等）。塌成单列后每个字段
    // 都能占满整行。`useBreakpoints()` 已处理 SSR/无 window 场景——`isSm`
    // 在非浏览器环境为 false，故服务端渲染仍按调用方传入的列数。
    //
    // `columns: 'auto'` 不受此影响：它的列数由容器宽度决定，窄容器本来就
    // 自动折成一列，无需再按视口二次判断。
    const bp = useBreakpoints()

    /** Effective column setting for one block (section override > renderer). */
    function resolveCols(sectionCols?: number | 'auto'): number | 'auto' {
      const requested = sectionCols ?? props.columns ?? 1
      if (requested === 'auto') return 'auto'
      return bp.isSm.value ? 1 : requested
    }

    function tr(key: string | undefined, fallback: string): string {
      if (!key) return fallback
      if (!props.translate) return fallback
      const out = props.translate(key)
      // When the key is unresolved, callers' translators typically return
      // a humanised fallback - that's still better than the raw key, but
      // for raw string labels we want the user-supplied string to win.
      return out || fallback
    }

    function effectiveTypeOf(item: FormSchemaItem): FormSchemaFieldType {
      return item.typeFn ? item.typeFn(props.model) : item.type
    }

    function renderField(item: FormSchemaItem) {
      // View-mode UX: text-style fields (input / textarea / number) use the
      // native `readonly` prop so the field keeps normal text colour and only
      // blocks editing - `disabled` would grey-out the body, which made long
      // content (e.g. 6KB template bodies) unreadable. Non-text widgets
      // (select / switch / date) don't expose `readonly`, so we still use
      // `disabled` but the `.t-form-schema--compact` CSS overrides their
      // muted colour back to normal text. End result: every field looks
      // identical to edit mode, just non-interactive.
      const viewMode = props.readonly
      const value = props.model[item.key]
      // `model` is a shared reactive bag passed as `:model="formData"`, not a
      // v-model value: every consumer (TCrudPage's `#form` slot, the detail
      // hosts, TItemPage) reads its edits back off the same object. Emitting an
      // update event instead would force ~130 call sites to thread the write
      // back by hand.
      // eslint-disable-next-line vue/no-mutating-props
      const onUpdate = (v: unknown) => { props.model[item.key] = v }
      const effectiveType = effectiveTypeOf(item)
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
          // bodies) from blowing past the modal viewport - without them,
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
          // nothing - the surfaced value signals the missing editor to the dev.
          return h('span', { class: 't-form-schema__unknown' }, String(value ?? ''))
      }
    }

    /**
     * Value cell for the `descriptions` readonly layout. A custom renderer
     * still wins (it owns the field's presentation in both modes); the
     * builtins degrade to the text a reader actually wants: the selected
     * option's LABEL rather than its id, a word rather than a switch, a
     * calendar date rather than an epoch number.
     */
    function renderReadValue(item: FormSchemaItem): VNodeChild {
      const value = props.model[item.key]
      const effectiveType = effectiveTypeOf(item)
      const custom = props.fieldRenderers?.[effectiveType]
      if (custom) {
        return custom({ item, value, readonly: true, onUpdate: () => {}, translate: tr })
      }
      if (effectiveType === 'switch') {
        const on = value === true
        return h(
          NTag,
          { size: 'small', round: true, bordered: false, type: on ? 'success' : 'default' },
          { default: () => (on ? tr('admin.common.yes', 'Yes') : tr('admin.common.no', 'No')) },
        )
      }
      if (isEmptyValue(value)) return EMPTY_DASH
      if (effectiveType === 'textarea') {
        // Long bodies (template markup, notes, JSON blobs) get their own
        // scrolling block instead of pushing the rest of the record off-screen.
        return h('div', { class: 't-form-schema__read-block' }, String(value))
      }
      if (effectiveType === 'select') {
        const values = Array.isArray(value) ? value : [value]
        const labels = values.map((v) => {
          const opt = (item.options ?? []).find((o) => o.value === v)
          return opt ? tr(opt.labelKey, opt.label) : String(v)
        })
        return labels.join(', ')
      }
      if (effectiveType === 'date') return toDateText(value) || EMPTY_DASH
      return String(value)
    }

    // Resolve the effective column span for a single field.
    //  - single column  → always a full row
    //  - fixed columns  → honour a numeric span exactly, clamped to the track count
    //  - auto columns   → span 1, or the whole row for anything wider (the track
    //                     count is unknown, so `span 2` would overflow a 1-col fold)
    function fieldSpanStyle(item: FormSchemaItem, cols: number | 'auto'): string | undefined {
      if (cols === 1) return undefined
      const effectiveType = effectiveTypeOf(item)
      const wantsFull =
        item.span === 'full' ||
        (item.span === undefined && effectiveType === 'textarea')
      if (cols === 'auto') {
        if (wantsFull || (typeof item.span === 'number' && item.span >= 2)) return 'grid-column: 1 / -1;'
        return undefined
      }
      if (wantsFull) return `grid-column: span ${cols};`
      if (typeof item.span === 'number' && item.span > 0) {
        return `grid-column: span ${Math.min(item.span, cols)};`
      }
      return undefined
    }

    function gridStyle(cols: number | 'auto'): Record<string, string> | undefined {
      if (cols === 1) return undefined
      return {
        display: 'grid',
        gridTemplateColumns:
          cols === 'auto'
            ? `repeat(auto-fit, minmax(min(100%, ${props.minColumnWidth}px), 1fr))`
            : `repeat(${cols}, minmax(0, 1fr))`,
        columnGap: '16px',
        rowGap: '0',
      }
    }

    /** Fields that pass their `visible` predicate, in declaration order. */
    const visibleItems = computed(() =>
      props.schema.filter((item) => !item.visible || item.visible(props.model)),
    )

    /**
     * Group the visible fields into blocks. Every schema resolves to at least
     * one block; a schema with no `section` on any field (the legacy shape)
     * resolves to exactly one untitled block, so its markup is unchanged.
     */
    const blocks = computed(() => {
      const declared = props.sections ?? []
      if (declared.length === 0) {
        return [{ section: undefined as FormSchemaSection | undefined, items: visibleItems.value }]
      }
      const byKey = new Map<string, FormSchemaItem[]>()
      const orphans: FormSchemaItem[] = []
      for (const item of visibleItems.value) {
        if (item.section && declared.some((s) => s.key === item.section)) {
          const list = byKey.get(item.section) ?? []
          list.push(item)
          byKey.set(item.section, list)
        } else {
          orphans.push(item)
        }
      }
      const out: Array<{ section?: FormSchemaSection; items: FormSchemaItem[] }> = []
      // Fields that name no section lead, so a schema can mix "a couple of
      // headline fields" with grouped detail below without declaring a
      // section just to hold them.
      if (orphans.length > 0) out.push({ section: undefined, items: orphans })
      for (const section of declared) {
        const items = byKey.get(section.key)
        // An empty section (every field hidden by a `visible` predicate) must
        // not render a heading over nothing.
        if (items && items.length > 0) out.push({ section, items })
      }
      return out
    })

    function renderFormBlock(items: FormSchemaItem[], cols: number | 'auto'): VNode {
      const formChildren = items.map((item) => {
        const spanStyle = fieldSpanStyle(item, cols)
        const hint = tr(item.hintKey, item.hint ?? '')
        return h(
          NFormItem,
          {
            label: tr(item.labelKey, item.label),
            path: item.key,
            required: item.required,
            key: item.key,
            style: spanStyle,
          },
          {
            default: () =>
              hint
                ? h('div', { class: 't-form-schema__field' }, [
                    renderField(item),
                    h('div', { class: 't-form-schema__hint' }, hint),
                  ])
                : renderField(item),
          },
        )
      })
      return h(
        NForm,
        {
          // The marker class drives compact spacing via ./form-schema.css.
          // Applied unconditionally so single-column forms benefit too - the
          // pre-refactor naive-ui defaults (24px feedback block, 8px label
          // padding) were always too loose for admin CRUD modals.
          class: 't-form-schema--compact',
          style: gridStyle(cols),
          labelPlacement: cols === 1 ? undefined : 'top',
        } as Record<string, unknown>,
        { default: () => formChildren },
      )
    }

    function renderDescriptionBlock(items: FormSchemaItem[], cols: number | 'auto'): VNode {
      const descItems: DescriptionItem[] = items.map((item) => {
        const effectiveType = effectiveTypeOf(item)
        // A field with nothing in it hands TDescriptions no renderer, so the
        // list draws its own muted placeholder. Supplying `render` would return
        // the same dash but as ordinary body text, and "no value" would read
        // exactly like a real value.
        const hasCustom = !!props.fieldRenderers?.[effectiveType]
        const blank =
          !hasCustom && effectiveType !== 'switch' && isEmptyValue(props.model[item.key])
        return {
          key: item.key,
          label: item.label,
          labelKey: item.labelKey,
          ...(blank ? {} : { render: () => renderReadValue(item) }),
          span:
            item.span === 'full' || effectiveType === 'textarea'
              ? 'full'
              : typeof item.span === 'number' && item.span >= 2
                ? 'full'
                : undefined,
        } satisfies DescriptionItem
      })
      return h(TDescriptions, {
        items: descItems,
        translate: props.translate,
        minColumnWidth: props.minColumnWidth,
        // A single-column form stays single-column when read back.
        ...(cols === 1 ? { maxColumns: 1 } : {}),
      })
    }

    function renderSectionHead(section: FormSchemaSection): VNode | null {
      const label = tr(section.labelKey, section.label ?? '')
      const hint = tr(section.hintKey, section.hint ?? '')
      if (!label && !hint) return null
      return h('div', { class: 't-form-schema__section-head' }, [
        label
          ? h('div', { class: 't-form-schema__section-title' }, [
              section.icon ? h(TSvgIcon, { icon: section.icon, size: 15 }) : null,
              h('span', label),
            ])
          : null,
        hint ? h('p', { class: 't-form-schema__section-hint' }, hint) : null,
      ])
    }

    return () => {
      const useDescriptions = props.readonly && props.readonlyLayout === 'descriptions'
      const list = blocks.value

      // Legacy shape: no declared sections → render exactly what earlier
      // versions did (a bare NForm), so no consumer's `:deep()` selector or
      // snapshot breaks on an extra wrapper element.
      const only = list.length === 1 ? list[0] : undefined
      if (only && !only.section) {
        const cols = resolveCols()
        return useDescriptions
          ? renderDescriptionBlock(only.items, cols)
          : renderFormBlock(only.items, cols)
      }

      return h(
        'div',
        { class: 't-form-schema' },
        list.map((block) =>
          h('section', { class: 't-form-schema__section', key: block.section?.key ?? '__lead__' }, [
            block.section ? renderSectionHead(block.section) : null,
            useDescriptions
              ? renderDescriptionBlock(block.items, resolveCols(block.section?.columns))
              : renderFormBlock(block.items, resolveCols(block.section?.columns)),
          ]),
        ),
      )
    }
  },
})

export default TSchemaForm
