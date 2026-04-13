import { defineComponent, h } from 'vue';

/** Pass-through structural wrapper — renders a <div> with slots */
const createSlotWrapper = (name: string, defaultClass: string) =>
  defineComponent({
    name,
    inheritAttrs: true,
    setup(_, { slots, attrs }) {
      return () => h('div', { ...attrs, class: [defaultClass, attrs.class] }, slots.default?.());
    },
  });

export const CardHeader = createSlotWrapper('CardHeader', 'n-card-header');
export const CardTitle = createSlotWrapper('CardTitle', 'n-card-header__main');
export const CardDescription = createSlotWrapper('CardDescription', 'n-card-header__extra text-sm text-muted-foreground');
export const CardFooter = createSlotWrapper('CardFooter', 'n-card__footer flex items-center');
