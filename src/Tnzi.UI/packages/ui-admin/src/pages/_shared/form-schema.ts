import { defineComponent, h, type PropType } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect, NDatePicker } from 'naive-ui'

export type FormSchemaFieldType = 'text' | 'textarea' | 'number' | 'switch' | 'select' | 'date'

export interface FormSchemaItem {
  key: string
  label: string
  type: FormSchemaFieldType
  // When present, takes precedence over `type` and lets the field switch
  // its editor based on other model values (e.g. SettingValueType-aware
  // value editor: text/number/switch/textarea per sibling enum).
  typeFn?: (model: Record<string, unknown>) => FormSchemaFieldType
  required?: boolean
  placeholder?: string
  options?: Array<{ label: string; value: string | number }>
  visible?: (model: Record<string, unknown>) => boolean
  max?: number
  min?: number
}

interface Props {
  schema: FormSchemaItem[]
  model: Record<string, unknown>
  readonly: boolean
}

const TFormSchemaRenderer = defineComponent({
  name: 'TFormSchemaRenderer',
  props: {
    schema: { type: Array as PropType<FormSchemaItem[]>, required: true },
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    readonly: { type: Boolean, default: false },
  },
  setup(props: Props) {
    function renderField(item: FormSchemaItem) {
      const disabled = props.readonly
      const value = props.model[item.key]
      const onUpdate = (v: unknown) => { props.model[item.key] = v }
      const effectiveType = item.typeFn ? item.typeFn(props.model) : item.type
      switch (effectiveType) {
        case 'text':
          return h(NInput, { value: value as string | null, disabled, placeholder: item.placeholder, 'onUpdate:value': onUpdate })
        case 'textarea':
          return h(NInput, { value: value as string | null, disabled, type: 'textarea', placeholder: item.placeholder, 'onUpdate:value': onUpdate })
        case 'number':
          return h(NInputNumber, { value: value as number | null, disabled, min: item.min, max: item.max, 'onUpdate:value': onUpdate })
        case 'switch':
          return h(NSwitch, { value: value as boolean, disabled, 'onUpdate:value': onUpdate })
        case 'select':
          return h(NSelect, { value: value as string | number | null, disabled, options: item.options ?? [], 'onUpdate:value': onUpdate })
        case 'date':
          return h(NDatePicker, { value: value as number | null, disabled, type: 'date', 'onUpdate:value': onUpdate })
      }
    }

    return () =>
      h(NForm, {}, {
        default: () =>
          props.schema
            .filter((item) => !item.visible || item.visible(props.model))
            .map((item) =>
              h(NFormItem, { label: item.label, path: item.key, required: item.required, key: item.key }, {
                default: () => renderField(item),
              }),
            ),
      })
  },
})

export default TFormSchemaRenderer
