/**
 * `TFormSchemaRenderer` admin wrapper.
 *
 * The renderer (`TSchemaForm`) lives in `@tnzi/ui/components/form`
 * (sunk in 0.2.x). This admin wrapper:
 *   - Renames the default export to `TFormSchemaRenderer` so existing
 *     admin pages (~30 .vue + ~30 *-config.ts files) keep their
 *     `import TFormSchemaRenderer from '../../_shared/form-schema'`
 *     paths working unchanged.
 *   - Accepts an explicit `translate` prop so per-page page-scoped
 *     translators (e.g. `translatePageKey('ai.agents', k)`) pass straight
 *     through - falls back to a global-namespace `translatePageKey('', k)`
 *     resolver when the caller doesn't supply one.
 *   - Injects admin-specific built-in field renderers (`icon` → TIconPicker,
 *     `json` → TJsonEditor) so any config can use `type: 'icon'` / `type:
 *     'json'` as a first-class field without the page hand-wiring the control.
 *     A page can register more (or override these) via the `fieldRenderers`
 *     prop - the page-supplied map wins on key collision.
 *   - Re-exports the `FormSchemaItem` / `FieldRenderer` types so config files
 *     keep their existing import paths.
 *
 * New code outside the admin shell should import directly from
 * `@tnzi/ui`:
 *
 *   import { TSchemaForm, type FormSchemaItem } from '@tnzi/ui'
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { defineComponent, h, type PropType } from 'vue'
import { NInput, NSelect, type SelectOption } from 'naive-ui'
import {
  TSchemaForm,
  type FormSchemaItem,
  type FormSchemaSection,
  type FieldRenderer,
  type ReadonlyLayout,
} from '@tnzi/ui'
import TIconPicker from '../../components/inputs/TIconPicker.vue'
import TJsonEditor from '../../components/inputs/TJsonEditor.vue'
import { translatePageKey } from './translate'

export type {
  FormSchemaItem,
  FormSchemaSection,
  FormSchemaFieldType,
  FieldRenderer,
  FieldRenderContext,
  ReadonlyLayout,
} from '@tnzi/ui'

// Default admin translator - used only when the caller does NOT supply a
// `translate` prop. Resolves absolute `admin.*` keys via the shared
// dictionary; page-scoped callers should pass their own page-namespaced
// `translate` so `form.x` resolves to `admin.modules.{pageNs}.form.x`.
const defaultAdminTranslate = (key: string): string => translatePageKey('', key)

// Built-in admin field renderers - activate the rich inputs as first-class
// form-schema field types so configs can declare `type: 'icon'` / `type:
// 'json'` and the renderer wires the control (value/onUpdate) automatically.
const adminFieldRenderers: Record<string, FieldRenderer> = {
  icon: (ctx) =>
    ctx.readonly
      // Read-only mode: render the icon identifier as plain text (the picker is
      // a popover trigger, meaningless when non-interactive).
      ? h('span', { class: 't-form-schema__icon-view' }, String(ctx.value ?? '') || EMPTY_DASH)
      : h(TIconPicker, {
          value: (ctx.value as string) ?? '',
          'onUpdate:value': (v: string) => ctx.onUpdate(v),
        }),
  json: (ctx) =>
    h(TJsonEditor, {
      value: (ctx.value as string) ?? '',
      readonly: ctx.readonly,
      'onUpdate:value': (v: string) => ctx.onUpdate(v),
    }),
  // Masked password input (click-to-reveal) - lets a create form declare
  // `type: 'password'` instead of hand-wiring an NInput. Read-only view mode
  // never echoes a password back, so it degrades to a muted dash.
  password: (ctx) =>
    ctx.readonly
      ? h('span', { class: 't-form-schema__password-view' }, '••••••')
      : h(NInput, {
          type: 'password',
          showPasswordOn: 'click',
          value: (ctx.value as string) ?? '',
          'onUpdate:value': (v: string) => ctx.onUpdate(v),
        }),
}

export interface SelectRendererOptions {
  /** Already-translated placeholder text (the page owns t()). */
  placeholder?: string
  /** Multi-select (tags). Empty selection normalises to `[]`. */
  multiple?: boolean
  /** Client-side type-ahead filtering (default true). */
  filterable?: boolean
  /** Show the clear affordance (default true). */
  clearable?: boolean
  /**
   * Allow typing a value that is not in the options (NSelect `tag`).
   * Use when the options list is a best-effort catalogue that may be
   * unavailable to the current user (e.g. loaded from an endpoint behind a
   * different permission) - the field then degrades to free entry instead
   * of becoming uneditable.
   */
  tag?: boolean
}

/**
 * Shared field renderer for an `NSelect` bound to **dynamic/reactive options** -
 * the case form-schema's declarative `type: 'select'` can't cover because its
 * options are loaded async (parent lists, account trees, tax agencies, …).
 *
 * Replaces the near-identical `h(NSelect, { value, options, placeholder,
 * filterable, clearable, disabled, onUpdate })` block that ~6 pages hand-wrote
 * (Menus / Accounts / FunctionModules / Items / Payments / Taxes). The page
 * keeps declaring the business part - WHICH options via a `() => options`
 * getter (so it stays reactive) and the resolved placeholder - while the
 * NSelect wiring (value coercion, clear/filter, readonly → disabled, update
 * normalisation) is unified here.
 *
 * ```ts
 * const fieldRenderers = {
 *   'fm-parent': selectRenderer(() => parentOptions.value, { placeholder: t('form.parentIdPlaceholder') }),
 *   'tags':      selectRenderer(() => tagOptions.value, { multiple: true }),
 * }
 * ```
 */
export function selectRenderer(
  getOptions: () => SelectOption[],
  opts: SelectRendererOptions = {},
): FieldRenderer {
  const { placeholder, multiple = false, filterable = true, clearable = true, tag = false } = opts
  return (ctx) =>
    // Cast to a loose record - naive's `options` prop type (SelectMixedOption)
    // over-constrains our plain `{ label, value }` list; mirrors _selector-factory.
    h(NSelect, {
      value: multiple
        ? (Array.isArray(ctx.value) ? (ctx.value as Array<string | number>) : [])
        : ((ctx.value as string | number | null) ?? null),
      options: getOptions(),
      multiple,
      placeholder,
      filterable,
      clearable,
      tag,
      disabled: ctx.readonly,
      'onUpdate:value': (v: unknown) =>
        ctx.onUpdate(multiple ? (v ?? []) : (v ?? undefined)),
    } as Record<string, unknown>)
}

const TFormSchemaRenderer = defineComponent({
  name: 'TFormSchemaRenderer',
  props: {
    schema: { type: Array as PropType<FormSchemaItem[]>, required: true },
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    readonly: { type: Boolean, default: false },
    /**
     * Column layout. Defaults to `'auto'`: the grid folds to as many columns as
     * the HOST surface can hold (`repeat(auto-fit, minmax(240px, 1fr))`), so a
     * 560px modal gets 2 columns, a 900px detail panel 3, and a phone 1,
     * without any page declaring breakpoints.
     *
     * Why auto is the default: admin forms used to render one field per row
     * regardless of how much room they had, which turned an 8-field record into
     * a scroll and read like a database row editor. A fixed number (`2` / `3`)
     * still wins when a page wants an exact grid.
     */
    columns: { type: [Number, String] as PropType<number | 'auto'>, default: 'auto' },
    /** Titled field blocks. Fields opt in via `FormSchemaItem.section`. */
    sections: { type: Array as PropType<FormSchemaSection[]>, default: undefined },
    /**
     * Shape of the read-only rendering. Defaults to `'descriptions'`: a view
     * action renders the record as `label: value` rows rather than as a form of
     * switched-off inputs. Pass `'inputs'` to keep the legacy shape (e.g. a page
     * whose view mode is really "edit, temporarily locked").
     */
    readonlyLayout: { type: String as PropType<ReadonlyLayout>, default: 'descriptions' },
    /** Minimum column width in `columns: 'auto'` mode. */
    minColumnWidth: { type: Number, default: 240 },
    translate: { type: Function as PropType<(key: string) => string>, default: undefined },
    /** Extra/override field renderers, merged over the admin built-ins. */
    fieldRenderers: { type: Object as PropType<Record<string, FieldRenderer>>, default: undefined },
  },
  setup(props) {
    return () =>
      h(TSchemaForm, {
        schema: props.schema,
        model: props.model,
        readonly: props.readonly,
        columns: props.columns,
        sections: props.sections,
        readonlyLayout: props.readonlyLayout,
        minColumnWidth: props.minColumnWidth,
        translate: props.translate ?? defaultAdminTranslate,
        fieldRenderers: { ...adminFieldRenderers, ...(props.fieldRenderers ?? {}) },
      })
  },
})

export default TFormSchemaRenderer
