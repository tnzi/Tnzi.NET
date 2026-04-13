import { defineComponent, h } from 'vue';
import { NInput } from 'naive-ui';

/**
 * Textarea primitive — wraps NInput with type="textarea".
 */
export const Textarea = defineComponent({
  name: 'Textarea',
  inheritAttrs: true,
  props: {
    modelValue: { type: String, default: undefined },
    placeholder: { type: String, default: undefined },
    rows: { type: Number, default: 3 },
    disabled: { type: Boolean, default: false },
  },
  emits: ['update:modelValue'],
  setup(props, { attrs, emit }) {
    return () =>
      h(NInput, {
        ...attrs,
        type: 'textarea',
        value: props.modelValue,
        placeholder: props.placeholder,
        rows: props.rows,
        disabled: props.disabled,
        'onUpdate:value': (v: string) => emit('update:modelValue', v),
      });
  },
});
