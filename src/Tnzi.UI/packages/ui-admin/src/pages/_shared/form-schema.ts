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
 *     through — falls back to a global-namespace `translatePageKey('', k)`
 *     resolver when the caller doesn't supply one.
 *   - Re-exports the `FormSchemaItem` type so config files keep their
 *     existing `import type { FormSchemaItem } from '_shared/form-schema'`
 *     paths.
 *
 * New code outside the admin shell should import directly from
 * `@tnzi/ui`:
 *
 *   import { TSchemaForm, type FormSchemaItem } from '@tnzi/ui'
 */
import { defineComponent, h, type PropType } from 'vue'
import { TSchemaForm, type FormSchemaItem } from '@tnzi/ui'
import { translatePageKey } from './translate'

export type { FormSchemaItem, FormSchemaFieldType } from '@tnzi/ui'

// Default admin translator — used only when the caller does NOT supply a
// `translate` prop. Resolves absolute `admin.*` keys via the shared
// dictionary; page-scoped callers should pass their own page-namespaced
// `translate` so `form.x` resolves to `admin.modules.{pageNs}.form.x`.
const defaultAdminTranslate = (key: string): string => translatePageKey('', key)

const TFormSchemaRenderer = defineComponent({
  name: 'TFormSchemaRenderer',
  props: {
    schema: { type: Array as PropType<FormSchemaItem[]>, required: true },
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    readonly: { type: Boolean, default: false },
    columns: { type: Number, default: 1 },
    translate: { type: Function as PropType<(key: string) => string>, default: undefined },
  },
  setup(props) {
    return () =>
      h(TSchemaForm, {
        schema: props.schema,
        model: props.model,
        readonly: props.readonly,
        columns: props.columns,
        translate: props.translate ?? defaultAdminTranslate,
      })
  },
})

export default TFormSchemaRenderer
