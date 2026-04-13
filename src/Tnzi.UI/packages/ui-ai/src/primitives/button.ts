import { defineComponent, h, type PropType } from 'vue';
import { NButton } from 'naive-ui';

/**
 * Button primitive — wraps NButton with shadcn size mapping.
 *
 * shadcn sizes: "sm" | "icon-sm" | "icon" | "lg" | "default"
 * naive-ui sizes: "tiny" | "small" | "medium" | "large"
 */
export type ButtonSize = 'sm' | 'icon-sm' | 'icon' | 'lg' | 'default' | 'tiny' | 'small' | 'medium' | 'large';

const sizeMap: Record<string, string> = {
  sm: 'small',
  'icon-sm': 'small',
  icon: 'small',
  lg: 'large',
  default: 'medium',
};

export const Button = defineComponent({
  name: 'Button',
  inheritAttrs: true,
  props: {
    size: { type: String as PropType<ButtonSize>, default: undefined },
    variant: { type: String, default: undefined },
    disabled: { type: Boolean, default: false },
    loading: { type: Boolean, default: false },
    type: { type: String, default: undefined },
  },
  setup(props, { slots, attrs }) {
    return () => {
      const mappedSize = props.size ? (sizeMap[props.size] ?? props.size) : undefined;
      const isCircle = props.size === 'icon' || props.size === 'icon-sm';
      const isGhost = props.variant === 'ghost';
      const isLink = props.variant === 'link';

      return h(
        NButton,
        {
          ...attrs,
          size: mappedSize as any,
          circle: isCircle || undefined,
          quaternary: isGhost || undefined,
          text: isLink || undefined,
          disabled: props.disabled,
          loading: props.loading,
          type: props.type as any,
        },
        slots,
      );
    };
  },
});
